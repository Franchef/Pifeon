using System.Text;
using System.Text.Json;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class PeerAndServerMessageChannelTests
{
    [Fact]
    public async Task PeerMessageChannel_ReceiveMessageAsync_ShouldDeserializeMessage()
    {
        var transportMock = new Mock<ITransportChannel>();

        PeerNetworkMessage expected = new(PeerNetworkMessageType.Manifest, 7, "hello"u8.ToArray());
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(expected, SignalingJsonContext.Default.PeerNetworkMessage);

        transportMock
            .Setup(t => t.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<Memory<byte>, CancellationToken>((buffer, _) =>
            {
                serialized.CopyTo(buffer);
                return ValueTask.FromResult(serialized.Length);
            });

        var sut = new PeerMessageChannel(transportMock.Object);

        PeerNetworkMessage actual = await sut.ReceiveMessageAsync(TestContext.Current.CancellationToken);

        Assert.Equal(expected.Type, actual.Type);
        Assert.Equal(expected.SequenceNumber, actual.SequenceNumber);
        Assert.Equal(expected.Payload.ToArray(), actual.Payload.ToArray());
    }

    [Fact]
    public async Task PeerMessageChannel_ReceiveMessageAsync_ShouldThrow_WhenTransportReturnsZeroBytes()
    {
        var transportMock = new Mock<ITransportChannel>();
        transportMock
            .Setup(t => t.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<Memory<byte>, CancellationToken>((_, _) => new ValueTask<int>(0));

        var sut = new PeerMessageChannel(transportMock.Object);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sut.ReceiveMessageAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task ServerMessageChannel_ReceiveMessageAsync_ShouldFallbackToLegacyPayload()
    {
        var transportMock = new Mock<ITransportChannel>();

        byte[] legacyPayload = JsonSerializer.SerializeToUtf8Bytes(
            new CodeCreatedResponse("CODE_CREATED", "123456"),
            SignalingJsonContext.Default.CodeCreatedResponse);

        transportMock
            .Setup(t => t.ReceiveAsync(It.IsAny<Memory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<Memory<byte>, CancellationToken>((buffer, _) =>
            {
                legacyPayload.CopyTo(buffer);
                return ValueTask.FromResult(legacyPayload.Length);
            });

        var sut = new ServerMessageChannel(transportMock.Object);

        ServerNetworkMessage message = await sut.ReceiveMessageAsync(TestContext.Current.CancellationToken);

        Assert.Equal(ServerNetworkMessageType.Handshake, message.Type);
        Assert.Equal(legacyPayload, message.Payload.ToArray());
    }

    [Fact]
    public async Task ServerMessageChannel_SendMessageAsync_ShouldSendCloseAction_WhenMessageTypeIsClose()
    {
        var transportMock = new Mock<ITransportChannel>();
        byte[]? capturedPayload = null;

        transportMock
            .Setup(t => t.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<ReadOnlyMemory<byte>, CancellationToken>((payload, _) =>
            {
                capturedPayload = payload.ToArray();
                return ValueTask.CompletedTask;
            });

        var sut = new ServerMessageChannel(transportMock.Object);

        await sut.SendMessageAsync(new ServerNetworkMessage(ServerNetworkMessageType.Close, 0, ReadOnlyMemory<byte>.Empty), TestContext.Current.CancellationToken);

        Assert.NotNull(capturedPayload);
        Assert.Equal("{\"action\":\"CLOSE\"}", Encoding.UTF8.GetString(capturedPayload!));
    }

    [Fact]
    public async Task ServerMessageChannel_SendMessageAsync_ShouldPassThroughPayload_WhenAlreadyJson()
    {
        var transportMock = new Mock<ITransportChannel>();
        byte[]? capturedPayload = null;

        transportMock
            .Setup(t => t.SendAsync(It.IsAny<ReadOnlyMemory<byte>>(), It.IsAny<CancellationToken>()))
            .Returns<ReadOnlyMemory<byte>, CancellationToken>((payload, _) =>
            {
                capturedPayload = payload.ToArray();
                return ValueTask.CompletedTask;
            });

        byte[] jsonPayload = "{\"action\":\"JOIN\",\"code\":\"654321\"}"u8.ToArray();
        var sut = new ServerMessageChannel(transportMock.Object);

        await sut.SendMessageAsync(new ServerNetworkMessage(ServerNetworkMessageType.Handshake, 1, jsonPayload), TestContext.Current.CancellationToken);

        Assert.NotNull(capturedPayload);
        Assert.Equal(jsonPayload, capturedPayload);
    }
}
