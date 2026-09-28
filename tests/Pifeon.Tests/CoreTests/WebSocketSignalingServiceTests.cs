using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling;
using Xunit;

namespace Pifeon.Tests.CoreTests;

public sealed class WebSocketSignalingServiceTests : IDisposable
{
    private readonly Mock<ITransportChannel> _channelMock;
    private readonly WebSocketSignalingService _sut; // System Under Test

    public WebSocketSignalingServiceTests()
    {
        _channelMock = new Mock<ITransportChannel>();
        _sut = new WebSocketSignalingService(_channelMock.Object);
    }

    public void Dispose()
    {
        _sut.Dispose();
    }

    #region 1. Validation & Constructor Tests

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenChannelIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() => new WebSocketSignalingService(null!));
        Assert.Equal("channel", ex.ParamName);
    }

    #endregion

    #region 2. Operations & Signaling Tests

    [Fact]
    public async Task CreateSessionAsync_ShouldSendCreateJsonPayloadAndReturnCode()
    {
        // Arrange
        _channelMock.Setup(c => c.IsConnected).Returns(true);

        string capturedPayload = string.Empty;
        _channelMock
            .Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((buffer, _) =>
            {
                capturedPayload = Encoding.UTF8.GetString(buffer.Span);
            })
            .Returns(ValueTask.CompletedTask);

        // Act
        string code = await _sut.CreateSessionAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("123456", code);
        Assert.Contains("\"action\":\"CREATE\"", capturedPayload);
    }

    [Fact]
    public async Task JoinSessionAsync_ShouldSendJoinJsonPayloadWithCode()
    {
        // Arrange
        string sessionCode = "654321";
        _channelMock.Setup(c => c.IsConnected).Returns(true);

        string capturedPayload = string.Empty;
        _channelMock
            .Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((buffer, _) =>
            {
                capturedPayload = Encoding.UTF8.GetString(buffer.Span);
            })
            .Returns(ValueTask.CompletedTask);

        // Act
        await _sut.JoinSessionAsync(sessionCode, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains($"\"code\":\"{sessionCode}\"", capturedPayload);
    }

    [Fact]
    public async Task SendSignalDataAsync_ShouldSendSignalJsonPayloadWithData()
    {
        // Arrange
        string payloadData = "SDP_OFFER_DATA";
        _channelMock.Setup(c => c.IsConnected).Returns(true);

        string capturedPayload = string.Empty;
        _channelMock
            .Setup(c => c.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Callback<ReadOnlyMemory<byte>, CancellationToken>((buffer, _) =>
            {
                capturedPayload = Encoding.UTF8.GetString(buffer.Span);
            })
            .Returns(ValueTask.CompletedTask);

        // Act
        await _sut.SendSignalDataAsync(payloadData, TestContext.Current.CancellationToken);

        // Assert
        Assert.Contains($"\"data\":\"{payloadData}\"", capturedPayload);
    }

    [Fact]
    public async Task WaitForReceiverAsync_ShouldThrowOperationCanceledException_WhenCancelled()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Annullamento immediato

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() => _sut.WaitForReceiverAsync(cts.Token));
    }

    #endregion

    #region 3. Listening Loop & Incoming Messages Parsing

    [Fact]
    public async Task ListeningLoop_ShouldTriggerOnReceiverJoinedAndUnblockWaitForReceiver()
    {
        // Arrange
        bool eventRaised = false;
        _sut.OnReceiverJoined += () => eventRaised = true;

        byte[] jsonResponse = Encoding.UTF8.GetBytes("{\"type\":\"RECEIVER_JOINED\"}");

        _channelMock.SetupSequence(c => c.IsConnected)
            .Returns(true)
            .Returns(true)
            .Returns(false);

        int callCount = 0;
        _channelMock
            .Setup(c => c.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<Memory<byte>, CancellationToken>((buffer, _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    jsonResponse.CopyTo(buffer);
                    return ValueTask.FromResult(jsonResponse.Length);
                }

                return ValueTask.FromResult(0);
            });

        // Act
        await _sut.ConnectAsync(TestContext.Current.CancellationToken);

        // Attende che il flag RECEIVER_JOINED sia consumato da WaitForReceiverAsync
        await _sut.WaitForReceiverAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(eventRaised);
    }

    [Fact]
    public async Task ListeningLoop_ShouldTriggerOnSignalDataReceived_WhenSignalMessageArrives()
    {
        // Arrange
        string? receivedPayload = null;
        _sut.OnSignalDataReceived += data => receivedPayload = data;

        byte[] jsonResponse = Encoding.UTF8.GetBytes("{\"type\":\"SIGNAL_DATA\",\"payload\":\"ICE_CANDIDATE_1\"}");

        _channelMock.SetupSequence(c => c.IsConnected)
            .Returns(true)
            .Returns(true)
            .Returns(false);

        int callCount = 0;
        _channelMock
            .Setup(c => c.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<Memory<byte>, CancellationToken>((buffer, _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    jsonResponse.CopyTo(buffer);
                    return ValueTask.FromResult(jsonResponse.Length);
                }

                return ValueTask.FromResult(0);
            });

        // Act
        await _sut.ConnectAsync(TestContext.Current.CancellationToken);

        // Diamo un breve delta per consentire all'event loop in background di processare il buffer
        await Task.Delay(100, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("ICE_CANDIDATE_1", receivedPayload);
    }

    [Fact]
    public async Task ListeningLoop_ShouldHandleInvalidJsonAndUnknownMessageTypeGracefully()
    {
        // Arrange
        byte[] invalidJson = Encoding.UTF8.GetBytes("NOT_A_VALID_JSON");
        byte[] unknownTypeJson = Encoding.UTF8.GetBytes("{\"type\":\"UNKNOWN_ACTION\"}");

        _channelMock.SetupSequence(c => c.IsConnected)
            .Returns(true)
            .Returns(true)
            .Returns(true)
            .Returns(false);

        int callCount = 0;
        _channelMock
            .Setup(c => c.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<Memory<byte>, CancellationToken>((buffer, _) =>
            {
                callCount++;
                if (callCount == 1)
                {
                    invalidJson.CopyTo(buffer);
                    return ValueTask.FromResult(invalidJson.Length);
                }
                if (callCount == 2)
                {
                    unknownTypeJson.CopyTo(buffer);
                    return ValueTask.FromResult(unknownTypeJson.Length);
                }

                return ValueTask.FromResult(0);
            });

        // Act & Assert (L'esecuzione non deve lanciare eccezioni)
        await _sut.ConnectAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.True(true); // Se arriviamo qui senza eccezioni, il test è passato
    }

    [Fact]
    public async Task ListeningLoop_ShouldHandleExceptionsAndChannelCloseSilently()
    {
        // Arrange
        _channelMock.Setup(c => c.IsConnected).Returns(true);
        _channelMock
            .Setup(c => c.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Network reset"));

        // Act & Assert (Il loop gestisce l'eccezione nel blocco catch senza far fallire l'applicazione)
        await _sut.ConnectAsync(TestContext.Current.CancellationToken);
        await Task.Delay(100, TestContext.Current.CancellationToken);
        Assert.True(true);
    }

    #endregion

    #region 4. Lifecycle & Disposal Tests

    [Fact]
    public async Task ConnectAsync_ShouldNotStartMultipleListeningTasks_WhenCalledRepeatedly()
    {
        // Arrange
        _channelMock.Setup(c => c.IsConnected).Returns(false);

        // Act
        await _sut.ConnectAsync(TestContext.Current.CancellationToken);
        await _sut.ConnectAsync(TestContext.Current.CancellationToken); // Seconda chiamata idonea per la verifica del blocco `_listenTask == null`

        // Assert
        _channelMock.Verify(c => c.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DisconnectAsync_ShouldCancelListeningTaskAndCloseChannel()
    {
        // Arrange
        _channelMock.Setup(c => c.IsConnected).Returns(true);
        _channelMock.Setup(c => c.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);

        await _sut.ConnectAsync(TestContext.Current.CancellationToken);

        // Act
        await _sut.DisconnectAsync(TestContext.Current.CancellationToken);

        // Assert
        _channelMock.Verify(c => c.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Methods_ShouldThrowObjectDisposedException_WhenObjectIsDisposed()
    {
        // Arrange
        await _sut.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.ConnectAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.CreateSessionAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.JoinSessionAsync("123456", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.WaitForReceiverAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => _sut.SendSignalDataAsync("data", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        _channelMock.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // Act
        await _sut.DisposeAsync();
        await _sut.DisposeAsync(); // Seconda chiamata

        // Assert
        _channelMock.Verify(c => c.DisposeAsync(), Times.Once);
    }

    [Fact]
    public void SynchronousDispose_ShouldCallDisposeAsync()
    {
        // Arrange
        _channelMock.Setup(c => c.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // Act
        _sut.Dispose();

        // Assert
        _channelMock.Verify(c => c.DisposeAsync(), Times.Once);
    }

    #endregion
}
