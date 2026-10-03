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
        await File.WriteAllBytesAsync(sampleFilePath, new byte[] { 0x01, 0x02, 0x03, 0x04 });

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

    #region 3. Lifecycle & Disposal Tests

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
