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
        Assert.NotEmpty(progressEvents.Where(e => e.current == 4)); // Final progress
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

        PeerNetworkMessage ackMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.ChunkAck,
            1,
            ReadOnlyMemory<byte>.Empty
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(ackMessage);

        // Act
        await _sut.SendAsync(_testDirectory, cts.Token);

        // Assert: Should send 2 chunks (one for each file)
        Assert.Equal(2, chunkSendCount);
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
