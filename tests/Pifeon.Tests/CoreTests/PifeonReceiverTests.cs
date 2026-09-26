using System;
using System.Collections.Generic;
using System.Text;
using System.Timers;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonReceiverTests : IDisposable
{
    private readonly Mock<ISignalingService> _signalingServiceMock;
    private readonly Mock<ITransportChannel> _dataChannelMock;
    private readonly PifeonReceiver _sut; // System Under Test

    public PifeonReceiverTests()
    {
        _signalingServiceMock = new Mock<ISignalingService>();
        _dataChannelMock = new Mock<ITransportChannel>();

        _sut = new PifeonReceiver(_signalingServiceMock.Object, _dataChannelMock.Object);
    }

    public void Dispose()
    {
        _sut.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    #region 1. Validation & Constructor Tests

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenSignalingServiceIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonReceiver(null!, _dataChannelMock.Object));

        Assert.Equal("signalingService", ex.ParamName);
    }

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenDataChannelIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonReceiver(_signalingServiceMock.Object, null!));

        Assert.Equal("dataChannel", ex.ParamName);
    }

    #endregion

    #region 2. Method Execution Tests

    [Fact]
    public async Task ConnectAndGetManifestAsync_ShouldCallJoinSessionAsyncAndReturnManifest()
    {
        // Arrange
        string sessionCode = "123456";
        using var cts = new CancellationTokenSource();

        _signalingServiceMock
            .Setup(s => s.JoinSessionAsync(sessionCode, cts.Token))
            .Returns(Task.CompletedTask)
            .Verifiable();

        // Act
        TransferManifest manifest = await _sut.ConnectAndGetManifestAsync(sessionCode, cts.Token);

        // Assert
        Assert.NotNull(manifest);
        _signalingServiceMock.Verify(s => s.JoinSessionAsync(sessionCode, cts.Token), Times.Once);
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        string destinationDir = @"C:\Downloads\Pifeon";
        using var cts = new CancellationTokenSource();

        // Act & Assert (verifica che non vengano sollevate eccezioni)
        await _sut.ReceiveToDirectoryAsync(destinationDir, cts.Token);
    }

    #endregion

    #region 3. Lifecycle & Disposal Tests

    [Fact]
    public async Task DisposeAsync_ShouldDisposeDataChannelAndSignalingService()
    {
        // Arrange
        _dataChannelMock
            .Setup(c => c.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        _signalingServiceMock
            .Setup(s => s.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        // Act
        await _sut.DisposeAsync();

        // Assert
        _dataChannelMock.Verify(c => c.DisposeAsync(), Times.Once);
        _signalingServiceMock.Verify(s => s.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        _dataChannelMock.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _signalingServiceMock.Setup(s => s.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // Act
        await _sut.DisposeAsync();
        await _sut.DisposeAsync(); // Seconda chiamata

        // Assert: I metodi DisposeAsync delle dipendenze devono essere stati invocati esattamente una sola volta
        _dataChannelMock.Verify(c => c.DisposeAsync(), Times.Once);
        _signalingServiceMock.Verify(s => s.DisposeAsync(), Times.Once);
    }

    #endregion
}
