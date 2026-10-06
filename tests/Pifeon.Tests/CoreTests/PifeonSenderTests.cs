using System.Text.Json;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonSenderTests : IDisposable
{
    private readonly Mock<IPeerMessageChannel> _peerChannelMock;
    private readonly PifeonSender _sut; // System Under Test
    private readonly string _testDirectory;

    public PifeonSenderTests()
    {
        _peerChannelMock = new Mock<IPeerMessageChannel>();

        _sut = new PifeonSender(_peerChannelMock.Object, "test-code");

        // Temporary folder for filesystem scan tests
        _testDirectory = Path.Combine(Path.GetTempPath(), "Pifeon_SenderTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    #region 1. Validation & Constructor Tests

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenPeerChannelIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonSender(null!, "test-code"));

        Assert.Equal("peerChannel", ex.ParamName);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenCodeIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonSender(_peerChannelMock.Object, null!));

        Assert.Equal("code", ex.ParamName);
    }

    #endregion

    #region 2. Session Initialization & Send Tests

    [Fact]
    public async Task InitializeSessionAsync_ShouldReturnCode()
    {
        // Arrange
        using var cts = new CancellationTokenSource();

        // Act
        string code = await _sut.InitializeSessionAsync(cts.Token);

        // Assert
        Assert.Equal("test-code", code);
    }

    [Fact]
    public async Task SendAsync_ShouldSendManifestAndChunks()
    {
        // Arrange
        string sampleFilePath = Path.Combine(_testDirectory, "data.bin");
        await File.WriteAllBytesAsync(sampleFilePath, new byte[] { 0x01, 0x02, 0x03, 0x04 }, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();

        // Setup mock to return ChunkAck for each chunk sent
        _peerChannelMock
            .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
            .Returns(ValueTask.CompletedTask);

        PeerNetworkMessage ackMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.ChunkAck,
            1,
            ReadOnlyMemory<byte>.Empty
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(ackMessage);

        // Act
        await _sut.SendAsync(sampleFilePath, cts.Token);

        // Assert: Verify manifest was sent
        _peerChannelMock.Verify(
            p => p.SendMessageAsync(It.Is<PeerNetworkMessage>(m => m.Type == PeerNetworkMessageType.Manifest), cts.Token),
            Times.Once);

        // Verify chunk data was sent
        _peerChannelMock.Verify(
            p => p.SendMessageAsync(It.Is<PeerNetworkMessage>(m => m.Type == PeerNetworkMessageType.ChunkData), cts.Token),
            Times.Once);
    }

    #endregion

    #region 3. Error Scenarios & Edge Cases

    [Fact]
    public async Task SendAsync_ShouldThrowInvalidOperationException_WhenCodeIsEmpty()
    {
        // Arrange
        var senderWithoutCode = new PifeonSender(_peerChannelMock.Object, "");
        string testFile = Path.Combine(_testDirectory, "test.bin");
        await File.WriteAllBytesAsync(testFile, new byte[] { 1, 2, 3 }, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() => senderWithoutCode.SendAsync(testFile, cts.Token));
    }

    [Fact]
    public async Task SendAsync_ShouldThrowFileNotFoundException_WhenPathDoesNotExist()
    {
        // Arrange
        string nonExistentPath = Path.Combine(_testDirectory, "doesnotexist.bin");
        using var cts = new CancellationTokenSource();

        // Act & Assert
        await Assert.ThrowsAsync<FileNotFoundException>(() => _sut.SendAsync(nonExistentPath, cts.Token));
    }

    [Fact]
    public async Task SendAsync_ShouldThrowInvalidOperationException_WhenReceiverSendsUnexpectedMessageType()
    {
        // Arrange
        string testFile = Path.Combine(_testDirectory, "data.bin");
        await File.WriteAllBytesAsync(testFile, new byte[] { 0x01, 0x02, 0x03, 0x04 }, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();

        _peerChannelMock
            .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
            .Returns(ValueTask.CompletedTask);

        // Return an invalid message type instead of ChunkAck
        PeerNetworkMessage invalidMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,  // Wrong type
            1,
            ReadOnlyMemory<byte>.Empty
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(invalidMessage);

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.SendAsync(testFile, cts.Token));
        Assert.Contains("Expected ChunkAck", ex.Message);
    }

    [Fact]
    public async Task SendAsync_ShouldReportProgressChanges()
    {
        // Arrange
        string testFile = Path.Combine(_testDirectory, "progress.bin");
        await File.WriteAllBytesAsync(testFile, new byte[] { 0x01, 0x02, 0x03, 0x04 }, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();

        var progressEvents = new List<(long current, long total, string fileName)>();

        _sut.OnProgressChanged += (current, total, fileName) =>
        {
            progressEvents.Add((current, total, fileName));
        };

        _peerChannelMock
            .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
            .Returns(ValueTask.CompletedTask);

        PeerNetworkMessage ackMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.ChunkAck,
            1,
            ReadOnlyMemory<byte>.Empty
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(ackMessage);

        // Act
        await _sut.SendAsync(testFile, cts.Token);

        // Assert: Progress events should have been raised
        Assert.NotEmpty(progressEvents);
        Assert.All(progressEvents, e => Assert.Equal(4, e.total)); // Total bytes
        Assert.Contains(progressEvents, e => e.current == 4);
    }

    [Fact]
    public async Task SendAsync_ShouldSendCorrectManifestFormat()
    {
        // Arrange
        string testFile = Path.Combine(_testDirectory, "manifest_test.bin");
        byte[] testData = new byte[] { 0x01, 0x02, 0x03 };
        await File.WriteAllBytesAsync(testFile, testData, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();

        PeerNetworkMessage? capturedManifest = null;

        _peerChannelMock
            .Setup(p => p.SendMessageAsync(It.Is<PeerNetworkMessage>(m => m.Type == PeerNetworkMessageType.Manifest), cts.Token))
            .Callback((PeerNetworkMessage msg, CancellationToken _) => capturedManifest = msg)
            .Returns(ValueTask.CompletedTask);

        _peerChannelMock
            .Setup(p => p.SendMessageAsync(It.Is<PeerNetworkMessage>(m => m.Type == PeerNetworkMessageType.ChunkData), cts.Token))
            .Returns(ValueTask.CompletedTask);

        PeerNetworkMessage ackMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.ChunkAck,
            1,
            ReadOnlyMemory<byte>.Empty
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(ackMessage);

        // Act
        await _sut.SendAsync(testFile, cts.Token);

        // Assert: Manifest should have been sent
        Assert.NotNull(capturedManifest);
        Assert.Equal(PeerNetworkMessageType.Manifest, capturedManifest!.Value.Type);

        // Deserialize and verify manifest content
        string manifestJson = System.Text.Encoding.UTF8.GetString(capturedManifest.Value.Payload.ToArray());
        TransferManifest? deserializedManifest = JsonSerializer.Deserialize(manifestJson, SignalingJsonContext.Default.TransferManifest);
        Assert.NotNull(deserializedManifest);
        Assert.Equal(1, deserializedManifest.TotalFiles);
        Assert.Equal(testData.Length, deserializedManifest.TotalSizeBytes);
    }

    [Fact]
    public async Task SendAsync_ShouldHandleMultipleFiles()
    {
        // Arrange
        string file1 = Path.Combine(_testDirectory, "file1.bin");
        string file2 = Path.Combine(_testDirectory, "file2.bin");
        await File.WriteAllBytesAsync(file1, new byte[] { 0x01, 0x02 }, TestContext.Current.CancellationToken);
        await File.WriteAllBytesAsync(file2, new byte[] { 0x03, 0x04, 0x05 }, TestContext.Current.CancellationToken);

        using var cts = new CancellationTokenSource();

        int chunkSendCount = 0;

        _peerChannelMock
            .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
            .Callback((PeerNetworkMessage msg, CancellationToken _) =>
            {
                if (msg.Type == PeerNetworkMessageType.ChunkData)
                {
                    chunkSendCount++;
                }
            })
            .Returns(ValueTask.CompletedTask);

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .Returns(() => ValueTask.FromResult(new PeerNetworkMessage(
                PeerNetworkMessageType.ChunkAck, chunkSendCount, ReadOnlyMemory<byte>.Empty)));

        // Act
        await _sut.SendAsync(_testDirectory, cts.Token);

        // Assert: Should send 2 chunks (one for each file)
        Assert.Equal(2, chunkSendCount);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SendAsync_ShouldRejectAcknowledgmentForWrongChunk(long sequence)
    {
        string file = Path.Combine(_testDirectory, "data.bin");
        await File.WriteAllBytesAsync(file, new byte[] { 0, 128, 255 }, TestContext.Current.CancellationToken);
        _peerChannelMock.Setup(channel => channel.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _peerChannelMock.Setup(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PeerNetworkMessage(PeerNetworkMessageType.ChunkAck, sequence, ReadOnlyMemory<byte>.Empty));

        InvalidDataException error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            _sut.SendAsync(file, TestContext.Current.CancellationToken));

        Assert.Contains("Expected acknowledgment for chunk 1", error.Message);
        _peerChannelMock.Verify(channel => channel.SendMessageAsync(
            It.Is<PeerNetworkMessage>(message => message.Type == PeerNetworkMessageType.ChunkData),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SendAsync_ShouldWaitForAcknowledgmentBeforeSendingNextChunk()
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        byte[] bytes = Enumerable.Range(0, 65537).Select(index => (byte)(index % 256)).ToArray();
        string file = Path.Combine(_testDirectory, "data.bin");
        await File.WriteAllBytesAsync(file, bytes, cts.Token);
        var firstChunkSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var firstAck = new TaskCompletionSource<PeerNetworkMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        var chunks = new List<PeerNetworkMessage>();
        _peerChannelMock.Setup(channel => channel.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
            .Callback<PeerNetworkMessage, CancellationToken>((message, _) =>
            {
                if (message.Type == PeerNetworkMessageType.ChunkData)
                {
                    chunks.Add(message with { Payload = message.Payload.ToArray() });
                    firstChunkSent.TrySetResult();
                }
            })
            .Returns(ValueTask.CompletedTask);
        _peerChannelMock.SetupSequence(channel => channel.ReceiveMessageAsync(cts.Token))
            .Returns(() => new ValueTask<PeerNetworkMessage>(firstAck.Task.WaitAsync(cts.Token)))
            .ReturnsAsync(new PeerNetworkMessage(PeerNetworkMessageType.ChunkAck, 2, ReadOnlyMemory<byte>.Empty));

        Task sending = _sut.SendAsync(file, cts.Token);
        await firstChunkSent.Task.WaitAsync(cts.Token);
        Assert.Single(chunks);
        Assert.False(sending.IsCompleted);
        firstAck.SetResult(new PeerNetworkMessage(PeerNetworkMessageType.ChunkAck, 1, ReadOnlyMemory<byte>.Empty));
        await sending.WaitAsync(cts.Token);

        Assert.Equal(new long[] { 1, 2 }, chunks.Select(chunk => chunk.SequenceNumber));
        Assert.Collection(chunks,
            first => Assert.Equal(65536, first.Payload.Length),
            second => Assert.Equal(1, second.Payload.Length));
        Assert.Equal(bytes, chunks.SelectMany(chunk => chunk.Payload.ToArray()).ToArray());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(65536, 1)]
    [InlineData(65537, 2)]
    [InlineData(131072, 2)]
    public async Task SendAsync_ShouldPreserveBytesAtChunkBoundaries(int fileSize, int expectedChunks)
    {
        byte[] bytes = Enumerable.Range(0, fileSize).Select(index => (byte)(index % 256)).ToArray();
        string file = Path.Combine(_testDirectory, "boundary.bin");
        await File.WriteAllBytesAsync(file, bytes, TestContext.Current.CancellationToken);
        var messages = new List<PeerNetworkMessage>();
        long lastSequence = 0;
        _peerChannelMock.Setup(channel => channel.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Callback<PeerNetworkMessage, CancellationToken>((message, _) =>
            {
                messages.Add(message with { Payload = message.Payload.ToArray() });
                if (message.Type == PeerNetworkMessageType.ChunkData)
                {
                    lastSequence = message.SequenceNumber;
                }
            })
            .Returns(ValueTask.CompletedTask);
        _peerChannelMock.Setup(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .Returns(() => ValueTask.FromResult(new PeerNetworkMessage(PeerNetworkMessageType.ChunkAck, lastSequence, ReadOnlyMemory<byte>.Empty)));

        await _sut.SendAsync(file, TestContext.Current.CancellationToken);

        Assert.Equal(PeerNetworkMessageType.Manifest, messages[0].Type);
        PeerNetworkMessage[] chunks = messages.Skip(1).ToArray();
        Assert.Equal(expectedChunks, chunks.Length);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(PeerNetworkMessageType.ChunkData, chunk.Type);
            Assert.InRange(chunk.Payload.Length, 1, PeerMessageChannel.MaximumChunkSize);
        });
        Assert.Equal(Enumerable.Range(1, expectedChunks).Select(value => (long)value), chunks.Select(chunk => chunk.SequenceNumber));
        Assert.Equal(bytes, chunks.SelectMany(chunk => chunk.Payload.ToArray()).ToArray());
        _peerChannelMock.Verify(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()), Times.Exactly(expectedChunks));
    }

    #endregion

    #region 4. Lifecycle & Disposal Tests

    [Fact]
    public async Task DisposeAsync_ShouldDisposePeerChannel()
    {
        // Arrange
        _peerChannelMock
            .Setup(p => p.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        // Act
        await _sut.DisposeAsync();

        // Assert
        _peerChannelMock.Verify(p => p.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        _peerChannelMock.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // Act
        await _sut.DisposeAsync();
        await _sut.DisposeAsync(); // Second call

        // Assert: DisposeAsync should be called only once
        _peerChannelMock.Verify(p => p.DisposeAsync(), Times.Once);
    }

    #endregion
}
