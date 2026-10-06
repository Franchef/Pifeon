using System.Text.Json;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonReceiverTests : IAsyncLifetime
{
    private readonly Mock<IPeerMessageChannel> _peer = new();
    private readonly Queue<PeerNetworkMessage> _incoming = new();
    private readonly List<PeerNetworkMessage> _sent = [];
    private readonly PifeonReceiver _receiver;
    private readonly string _destination = Path.Combine(Path.GetTempPath(), $"pifeon_receiver_{Guid.NewGuid():N}", "received");

    public PifeonReceiverTests()
    {
        _receiver = new PifeonReceiver(_peer.Object);
        _peer.Setup(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct =>
            {
                ct.ThrowIfCancellationRequested();
                return ValueTask.FromResult(_incoming.Dequeue());
            });
        _peer.Setup(channel => channel.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Callback<PeerNetworkMessage, CancellationToken>((message, _) => _sent.Add(message))
            .Returns(ValueTask.CompletedTask);
        _peer.Setup(channel => channel.DisposeAsync()).Returns(ValueTask.CompletedTask);
    }

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync()
    {
        await _receiver.DisposeAsync();
        string root = Path.GetDirectoryName(_destination)!;
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private void EnqueueManifest(TransferManifest manifest)
    {
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.Manifest, 0,
            JsonSerializer.SerializeToUtf8Bytes(manifest, SignalingJsonContext.Default.TransferManifest)));
    }

    private async Task SetManifestAsync(params TransferItemInfo[] files)
    {
        EnqueueManifest(new TransferManifest(files.Length, files.Sum(file => file.FileSize), files));
        await _receiver.ConnectAndGetManifestAsync("123456", TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Constructor_ShouldRejectNullPeer()
    {
        ArgumentNullException error = Assert.Throws<ArgumentNullException>(() => new PifeonReceiver(null!));
        Assert.Equal("peerChannel", error.ParamName);
    }

    [Fact]
    public async Task GetManifest_ShouldReturnFileMetadata()
    {
        var expected = new TransferManifest(1, 100, [new TransferItemInfo("test.txt", 100)]);
        EnqueueManifest(expected);
        TransferManifest actual = await _receiver.ConnectAndGetManifestAsync("123456", TestContext.Current.CancellationToken);

        Assert.Equal(expected.TotalFiles, actual.TotalFiles);
        Assert.Equal(expected.TotalSizeBytes, actual.TotalSizeBytes);
        Assert.Equal(expected.Items, actual.Items);
        _peer.Verify(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EmptyManifest_ShouldCreateDirectoryWithoutReceivingChunks()
    {
        await SetManifestAsync();
        await _receiver.ReceiveToDirectoryAsync(_destination, TestContext.Current.CancellationToken);

        Assert.True(Directory.Exists(_destination));
        Assert.Empty(Directory.GetFiles(_destination));
        Assert.Empty(_sent);
        _peer.Verify(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(PeerNetworkMessageType.ChunkData)]
    [InlineData(PeerNetworkMessageType.ChunkAck)]
    public async Task GetManifest_ShouldRejectUnexpectedMessage(PeerNetworkMessageType type)
    {
        _incoming.Enqueue(new PeerNetworkMessage(type, 1, "wrong"u8.ToArray()));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _receiver.ConnectAndGetManifestAsync("123456", TestContext.Current.CancellationToken));
        Assert.Contains("Expected Manifest", error.Message);
    }

    [Fact]
    public async Task GetManifest_ShouldReportPeerAbort()
    {
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.Abort, 0, "Transfer canceled"u8.ToArray()));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _receiver.ConnectAndGetManifestAsync("123456", TestContext.Current.CancellationToken));
        Assert.Equal("Transfer canceled", error.Message);
    }

    [Fact]
    public async Task GetManifest_ShouldRejectCorruptedJson()
    {
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.Manifest, 0, "{ invalid json }"u8.ToArray()));
        await Assert.ThrowsAsync<JsonException>(() => _receiver.ConnectAndGetManifestAsync("123456", TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData(2, 5, 5)]
    [InlineData(1, 6, 5)]
    [InlineData(1, -1, -1)]
    public async Task GetManifest_ShouldRejectInconsistentCountsOrSizes(int count, long total, long fileSize)
    {
        EnqueueManifest(new TransferManifest(count, total, [new TransferItemInfo("test.bin", fileSize)]));
        await Assert.ThrowsAsync<InvalidDataException>(() => _receiver.ConnectAndGetManifestAsync("123456", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Receive_ShouldRequireManifest()
    {
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _receiver.ReceiveToDirectoryAsync(_destination, TestContext.Current.CancellationToken));
        Assert.Contains("ConnectAndGetManifestAsync", error.Message);
        Assert.False(Directory.Exists(_destination));
    }

    [Fact]
    public async Task Receive_ShouldRejectUnexpectedChunkType()
    {
        await SetManifestAsync(new TransferItemInfo("test.bin", 5));
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.Manifest, 1, "hello"u8.ToArray()));
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _receiver.ReceiveToDirectoryAsync(_destination, TestContext.Current.CancellationToken));
        Assert.Contains("Expected ChunkData", error.Message);
        Assert.Empty(_sent);
    }

    [Theory]
    [InlineData(0, 5, 5)]
    [InlineData(2, 5, 5)]
    [InlineData(1, 0, 5)]
    [InlineData(1, 6, 5)]
    [InlineData(1, 65537, 65537)]
    public async Task Receive_ShouldRejectInvalidSequenceOrSizeBeforeWriting(long sequence, int chunkSize, long fileSize)
    {
        await SetManifestAsync(new TransferItemInfo("test.bin", fileSize));
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.ChunkData, sequence, new byte[chunkSize]));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _receiver.ReceiveToDirectoryAsync(_destination, TestContext.Current.CancellationToken));

        Assert.Empty(_sent);
        Assert.Equal(0, new FileInfo(Path.Combine(_destination, "test.bin")).Length);
    }

    [Theory]
    [InlineData("../outside.bin")]
    [InlineData("..\\outside.bin")]
    [InlineData("file.bin:stream")]
    public async Task Receive_ShouldRejectEscapingDestinationPaths(string relativePath)
    {
        await SetManifestAsync(new TransferItemInfo(relativePath, 0));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            _receiver.ReceiveToDirectoryAsync(_destination, TestContext.Current.CancellationToken));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(_destination)!, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Receive_ShouldPreserveFilesAndAcknowledgeGlobalChunkSequence()
    {
        await SetManifestAsync(new TransferItemInfo("nested/first.bin", 5),
            new TransferItemInfo("empty.bin", 0), new TransferItemInfo("second.bin", 3));
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.ChunkData, 1, "he"u8.ToArray()));
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.ChunkData, 2, "llo"u8.ToArray()));
        _incoming.Enqueue(new PeerNetworkMessage(PeerNetworkMessageType.ChunkData, 3, new byte[] { 0, 128, 255 }));
        var progress = new List<(long Current, long Total, string File)>();
        _receiver.OnProgressChanged += (current, total, file) => progress.Add((current, total, file));

        await _receiver.ReceiveToDirectoryAsync(_destination, TestContext.Current.CancellationToken);

        Assert.Equal("hello"u8.ToArray(), await File.ReadAllBytesAsync(Path.Combine(_destination, "nested", "first.bin"), TestContext.Current.CancellationToken));
        Assert.Equal(new byte[] { 0, 128, 255 }, await File.ReadAllBytesAsync(Path.Combine(_destination, "second.bin"), TestContext.Current.CancellationToken));
        Assert.Equal(0, new FileInfo(Path.Combine(_destination, "empty.bin")).Length);
        Assert.Equal(new long[] { 1, 2, 3 }, _sent.Select(message => message.SequenceNumber));
        Assert.All(_sent, message =>
        {
            Assert.Equal(PeerNetworkMessageType.ChunkAck, message.Type);
            Assert.True(message.Payload.IsEmpty);
        });
        Assert.Equal(new (long, long, string)[] { (2, 8, "nested/first.bin"), (5, 8, "nested/first.bin"), (8, 8, "second.bin") }, progress);
        Assert.Empty(_incoming);
    }

    [Fact]
    public async Task Dispose_ShouldDisposePeerOnce()
    {
        await _receiver.DisposeAsync();
        await _receiver.DisposeAsync();
        _peer.Verify(channel => channel.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task Dispose_WithSession_ShouldDelegateResourceOwnershipToSession()
    {
        var session = new Mock<ISession>();
        session.Setup(value => value.DisposeAsync()).Returns(ValueTask.CompletedTask);
        await using var receiver = new PifeonReceiver(_peer.Object, session.Object);
        await receiver.DisposeAsync();

        session.Verify(value => value.DisposeAsync(), Times.Once);
        _peer.Verify(channel => channel.DisposeAsync(), Times.Never);
    }
}
