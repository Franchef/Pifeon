using System.Text.Json;
using System.Threading.Channels;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Exceptions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonSessionTests : IDisposable
{
    private readonly Mock<IServerMessageChannel> _server = new();
    private readonly Mock<IPeerMessageChannel> _peer = new();
    private readonly Channel<ServerNetworkMessage> _messages = Channel.CreateUnbounded<ServerNetworkMessage>();
    private readonly CancellationTokenSource _timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    public PifeonSessionTests()
    {
        _timeout.CancelAfter(TimeSpan.FromSeconds(10));
        _server.Setup(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(ct => _messages.Reader.ReadAsync(ct));
        _server.Setup(channel => channel.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);
        _server.Setup(channel => channel.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _server.Setup(channel => channel.DisposeAsync()).Returns(ValueTask.CompletedTask);
        _peer.Setup(channel => channel.CloseAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _peer.Setup(channel => channel.DisposeAsync()).Returns(ValueTask.CompletedTask);
    }

    public void Dispose()
    {
        _timeout.Dispose();
    }

    private void EnqueueCreated(string code = "123456")
    {
        Assert.True(_messages.Writer.TryWrite(new ServerNetworkMessage(ServerNetworkMessageType.Handshake, 0,
            JsonSerializer.SerializeToUtf8Bytes(new CodeCreatedResponse("CODE_CREATED", code), SignalingJsonContext.Default.CodeCreatedResponse))));
    }

    private void EnqueueJoined()
    {
        Assert.True(_messages.Writer.TryWrite(new ServerNetworkMessage(ServerNetworkMessageType.IpExchanges, 0,
            JsonSerializer.SerializeToUtf8Bytes(new ReceiverJoinedResponse("RECEIVER_JOINED"), SignalingJsonContext.Default.ReceiverJoinedResponse))));
    }

    private void EnqueueError(string message)
    {
        Assert.True(_messages.Writer.TryWrite(new ServerNetworkMessage(ServerNetworkMessageType.Abort, 0,
            JsonSerializer.SerializeToUtf8Bytes(new ErrorResponse("ERROR", message), SignalingJsonContext.Default.ErrorResponse))));
    }

    [Fact]
    public async Task CreateSender_ShouldReadCodeWithoutSendingRedundantCreateHandshake()
    {
        EnqueueCreated();
        await using ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token);

        Assert.Equal("123456", session.Code);
        Assert.False(session.IsConnected);
        _server.Verify(channel => channel.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task CreateSender_ShouldReportRateLimitResponse()
    {
        EnqueueError("429 too many requests");
        await Assert.ThrowsAsync<CreateSessionRateLimitedException>(() =>
            PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token));
    }

    [Fact]
    public async Task CreateReceiver_WithInjectedPeer_ShouldNotSendRedundantJoinHandshake()
    {
        await using IReceiverSession session = await PifeonSession.CreateReceiverAsync("654321", _server.Object, _peer.Object, _timeout.Token);
        Assert.Equal("654321", session.Code);
        _server.Verify(channel => channel.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetSender_ShouldReturnBeforeReceiverJoins()
    {
        EnqueueCreated();
        await using ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token);

        ISender sender = await session.GetSenderAsync(_timeout.Token);

        Assert.Equal(session.Code, sender.Code);
        Assert.False(session.IsConnected);
        EnqueueJoined();
        await session.WaitUntilConnectedAsync(_timeout.Token);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task DirectSession_ShouldNotTreatReceiverJoinedAsConfirmedPeerConnection()
    {
        EnqueueCreated();
        EnqueueJoined();
        var joinedConsumed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Setup(channel => channel.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .Returns<CancellationToken>(async ct =>
            {
                ServerNetworkMessage message = await _messages.Reader.ReadAsync(ct);
                if (message.Type == ServerNetworkMessageType.IpExchanges)
                {
                    joinedConsumed.TrySetResult();
                }
                return message;
            });
        var endpointSent = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _server.Setup(channel => channel.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns<ServerNetworkMessage, CancellationToken>((message, _) =>
            {
                Assert.Equal(ServerNetworkMessageType.IpExchanges, message.Type);
                endpointSent.TrySetResult();
                return ValueTask.CompletedTask;
            });
        await using ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, new DirectPeerMessageChannel(), _timeout.Token);
        await endpointSent.Task.WaitAsync(_timeout.Token);
        await joinedConsumed.Task.WaitAsync(_timeout.Token);
        ISender sender = await session.GetSenderAsync(_timeout.Token);

        Assert.Equal(session.Code, sender.Code);
        Assert.False(session.IsConnected);
        _server.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DirectReceiver_ShouldWaitForEndpointAndKeyConfirmation()
    {
        await using IReceiverSession session = await PifeonSession.CreateReceiverAsync(
            "654321", _server.Object, new DirectPeerMessageChannel(), _timeout.Token);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        Assert.False(session.IsConnected);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.GetReceiverAsync(canceled.Token));
    }

    [Fact]
    public async Task GetSender_ShouldRejectReceiverSession()
    {
        await using IReceiverSession session = await PifeonSession.CreateReceiverAsync("111111", _server.Object, _peer.Object, _timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((ISenderSession)session).GetSenderAsync(_timeout.Token));
    }

    [Fact]
    public async Task GetReceiver_ShouldRejectSenderSession()
    {
        EnqueueCreated();
        await using ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => ((IReceiverSession)session).GetReceiverAsync(_timeout.Token));
    }

    [Fact]
    public async Task WaitUntilConnected_ShouldReportInvalidCodeResponse()
    {
        EnqueueCreated("999999");
        await using ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token);
        EnqueueError("Code invalid or expired");

        await Assert.ThrowsAsync<SessionNotFoundException>(() => session.WaitUntilConnectedAsync(_timeout.Token));
    }

    [Fact]
    public async Task CancelWait_ShouldNotCancelSubsequentConnectionWait()
    {
        EnqueueCreated();
        await using ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WaitUntilConnectedAsync(canceled.Token));

        EnqueueJoined();
        await session.WaitUntilConnectedAsync(_timeout.Token);
        Assert.True(session.IsConnected);
    }

    [Fact]
    public async Task Close_ShouldBeIdempotentAndClearConnectedState()
    {
        EnqueueCreated();
        await using ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token);
        EnqueueJoined();
        await session.WaitUntilConnectedAsync(_timeout.Token);

        await session.CloseAsync(_timeout.Token);
        await session.CloseAsync(_timeout.Token);

        Assert.False(session.IsConnected);
        _server.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
        _peer.Verify(channel => channel.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Dispose_ShouldCancelPendingListenerAndDisposeChannelsOnce()
    {
        EnqueueCreated();
        ISenderSession session = await PifeonSession.CreateSenderAsync(_server.Object, _peer.Object, _timeout.Token);
        await session.DisposeAsync();
        await session.DisposeAsync();

        _server.Verify(channel => channel.DisposeAsync(), Times.Once);
        _peer.Verify(channel => channel.DisposeAsync(), Times.Once);
        await Assert.ThrowsAsync<ObjectDisposedException>(() => session.WaitUntilConnectedAsync(_timeout.Token));
    }
}
