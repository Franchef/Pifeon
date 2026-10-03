using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Timers;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonReceiverTests : IDisposable
{
    private readonly Mock<ISignalingService> _signalingServiceMock;
    private readonly Mock<ITransportChannel> _dataChannelMock;
    private readonly Mock<IPeerMessageChannel> _peerChannelMock;
    private readonly PifeonReceiver _sut; // System Under Test

    public PifeonReceiverTests()
    {
        _signalingServiceMock = new Mock<ISignalingService>();
        _dataChannelMock = new Mock<ITransportChannel>();
        _peerChannelMock = new Mock<IPeerMessageChannel>();

        _sut = new PifeonReceiver(_peerChannelMock.Object);
    }

    public void Dispose()
    {
        _sut.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    #region 1. Validation & Constructor Tests

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenPeerChannelIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonReceiver(null!));

        Assert.Equal("peerChannel", ex.ParamName);
    }

    #endregion

    #region 2. Method Execution Tests

    [Fact]
    public async Task ConnectAndGetManifestAsync_ShouldReturnManifest()
    {
        // Arrange
        string sessionCode = "123456";
        using var cts = new CancellationTokenSource();

        // Setup the mock to return a manifest message
        TransferManifest expectedManifest = new TransferManifest(1, 100, 
            new List<TransferItemInfo> { new TransferItemInfo("test.txt", 100) });
        string manifestJson = JsonSerializer.Serialize(expectedManifest, SignalingJsonContext.Default.TransferManifest);
        byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,
            0,
            manifestPayload
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(manifestMessage)
            .Verifiable();

        // Act
        TransferManifest manifest = await _sut.ConnectAndGetManifestAsync(sessionCode, cts.Token);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal(expectedManifest.TotalFiles, manifest.TotalFiles);
        Assert.Equal(expectedManifest.TotalSizeBytes, manifest.TotalSizeBytes);
        _peerChannelMock.Verify(p => p.ReceiveMessageAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        string destinationDir = @"C:\Downloads\Pifeon";
        using var cts = new CancellationTokenSource();

        // Must call ConnectAndGetManifestAsync first
        TransferManifest manifest = new TransferManifest(0, 0, []);
        string manifestJson = JsonSerializer.Serialize(manifest, SignalingJsonContext.Default.TransferManifest);
        byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,
            0,
            manifestPayload
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(manifestMessage);

        await _sut.ConnectAndGetManifestAsync("123456", cts.Token);

        // Act & Assert (verifica che non vengano sollevate eccezioni)
        await _sut.ReceiveToDirectoryAsync(destinationDir, cts.Token);
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
