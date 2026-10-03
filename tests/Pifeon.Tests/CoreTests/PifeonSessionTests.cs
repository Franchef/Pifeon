using System.Text.Json;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Exceptions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonSessionTests
{
    [Fact]
    public async Task CreateSenderAsync_ShouldSetCode_WhenServerReturnsCodeCreated()
    {
        var serverChannelMock = new Mock<IServerMessageChannel>();
        var peerChannelMock = new Mock<IPeerMessageChannel>();

        byte[] responsePayload = JsonSerializer.SerializeToUtf8Bytes(
            new CodeCreatedResponse("CODE_CREATED", "123456"),
            SignalingJsonContext.Default.CodeCreatedResponse);

        serverChannelMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        serverChannelMock
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerNetworkMessage(ServerNetworkMessageType.Handshake, 0, responsePayload));

        PifeonSession session = await PifeonSession.CreateSenderAsync(serverChannelMock.Object, peerChannelMock.Object, TestContext.Current.CancellationToken);

        Assert.Equal("123456", session.Code);
    }

    [Fact]
    public async Task CreateSenderAsync_ShouldThrowCreateSessionRateLimitedException_WhenServerReturnsRateLimitError()
    {
        var serverChannelMock = new Mock<IServerMessageChannel>();
        var peerChannelMock = new Mock<IPeerMessageChannel>();

        byte[] errorPayload = JsonSerializer.SerializeToUtf8Bytes(
            new ErrorResponse("ERROR", "429 too many requests"),
            SignalingJsonContext.Default.ErrorResponse);

        serverChannelMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        serverChannelMock
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerNetworkMessage(ServerNetworkMessageType.Abort, 0, errorPayload));

        await Assert.ThrowsAsync<CreateSessionRateLimitedException>(() =>
            PifeonSession.CreateSenderAsync(serverChannelMock.Object, peerChannelMock.Object, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateReceiverAsync_ShouldSendJoinHandshakeWithCode()
    {
        var serverChannelMock = new Mock<IServerMessageChannel>();
        var peerChannelMock = new Mock<IPeerMessageChannel>();

        ServerNetworkMessage? captured = null;

        serverChannelMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns<ServerNetworkMessage, CancellationToken>((message, _) =>
            {
                captured = message;
                return ValueTask.CompletedTask;
            });

        PifeonSession session = await PifeonSession.CreateReceiverAsync("654321", serverChannelMock.Object, peerChannelMock.Object, TestContext.Current.CancellationToken);

        Assert.Equal("654321", session.Code);
        Assert.NotNull(captured);

        JoinSessionRequest? joinRequest = JsonSerializer.Deserialize(captured!.Value.Payload.Span, SignalingJsonContext.Default.JoinSessionRequest);
        Assert.NotNull(joinRequest);
        Assert.Equal("654321", joinRequest.Code);
    }

    [Fact]
    public async Task GetSenderAsync_ShouldWaitReceiverJoined_AndReturnSender()
    {
        var serverChannelMock = new Mock<IServerMessageChannel>();
        var peerChannelMock = new Mock<IPeerMessageChannel>();

        byte[] createPayload = JsonSerializer.SerializeToUtf8Bytes(
            new CodeCreatedResponse("CODE_CREATED", "123456"),
            SignalingJsonContext.Default.CodeCreatedResponse);

        byte[] receiverJoinedPayload = JsonSerializer.SerializeToUtf8Bytes(
            new ReceiverJoinedResponse("RECEIVER_JOINED"),
            SignalingJsonContext.Default.ReceiverJoinedResponse);

        serverChannelMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        serverChannelMock
            .SetupSequence(s => s.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerNetworkMessage(ServerNetworkMessageType.Handshake, 0, createPayload))
            .ReturnsAsync(new ServerNetworkMessage(ServerNetworkMessageType.IpExchanges, 1, receiverJoinedPayload));

        PifeonSession session = await PifeonSession.CreateSenderAsync(serverChannelMock.Object, peerChannelMock.Object, TestContext.Current.CancellationToken);

        ISender sender = await session.GetSenderAsync(TestContext.Current.CancellationToken);

        Assert.NotNull(sender);
        Assert.Equal("123456", sender.Code);
    }

    [Fact]
    public async Task GetSenderAsync_ShouldThrow_WhenSessionIsReceiverSide()
    {
        var serverChannelMock = new Mock<IServerMessageChannel>();
        var peerChannelMock = new Mock<IPeerMessageChannel>();

        serverChannelMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        PifeonSession session = await PifeonSession.CreateReceiverAsync("111111", serverChannelMock.Object, peerChannelMock.Object, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<InvalidOperationException>(() => session.GetSenderAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task WaitUntilConnectedAsync_ShouldThrowSessionNotFound_WhenServerReturnsInvalidCodeError()
    {
        var serverChannelMock = new Mock<IServerMessageChannel>();
        var peerChannelMock = new Mock<IPeerMessageChannel>();

        byte[] createPayload = JsonSerializer.SerializeToUtf8Bytes(
            new CodeCreatedResponse("CODE_CREATED", "999999"),
            SignalingJsonContext.Default.CodeCreatedResponse);

        byte[] errorPayload = JsonSerializer.SerializeToUtf8Bytes(
            new ErrorResponse("ERROR", "Code invalid or expired"),
            SignalingJsonContext.Default.ErrorResponse);

        serverChannelMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        serverChannelMock
            .SetupSequence(s => s.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerNetworkMessage(ServerNetworkMessageType.Handshake, 0, createPayload))
            .ReturnsAsync(new ServerNetworkMessage(ServerNetworkMessageType.Abort, 1, errorPayload));

        PifeonSession session = await PifeonSession.CreateSenderAsync(serverChannelMock.Object, peerChannelMock.Object, TestContext.Current.CancellationToken);

        await Assert.ThrowsAsync<SessionNotFoundException>(() => session.WaitUntilConnectedAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CloseAsync_ShouldBeIdempotent_AndCloseChannels()
    {
        var serverChannelMock = new Mock<IServerMessageChannel>();
        var peerChannelMock = new Mock<IPeerMessageChannel>();

        byte[] createPayload = JsonSerializer.SerializeToUtf8Bytes(
            new CodeCreatedResponse("CODE_CREATED", "123456"),
            SignalingJsonContext.Default.CodeCreatedResponse);

        serverChannelMock
            .Setup(s => s.SendMessageAsync(It.IsAny<ServerNetworkMessage>(), It.IsAny<CancellationToken>()))
            .Returns(ValueTask.CompletedTask);

        serverChannelMock
            .Setup(s => s.ReceiveMessageAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ServerNetworkMessage(ServerNetworkMessageType.Handshake, 0, createPayload));

        serverChannelMock
            .Setup(s => s.CloseAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        peerChannelMock
            .Setup(s => s.CloseAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask)
            .Verifiable();

        PifeonSession session = await PifeonSession.CreateSenderAsync(serverChannelMock.Object, peerChannelMock.Object, TestContext.Current.CancellationToken);

        await session.CloseAsync(TestContext.Current.CancellationToken);
        await session.CloseAsync(TestContext.Current.CancellationToken);

        serverChannelMock.Verify(s => s.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
        peerChannelMock.Verify(s => s.CloseAsync(It.IsAny<CancellationToken>()), Times.Once);
    }
}
