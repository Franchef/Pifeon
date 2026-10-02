using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Networking;

public sealed class PeerMessageChannel(ITransportChannel transportChannel) : IPeerMessageChannel
{
    public bool IsConnected => transportChannel.IsConnected; // Assuming ITransportChannel has an IsConnected property

    public Task CloseAsync(CancellationToken ct = default) => transportChannel.CloseAsync(ct);

    public ValueTask DisposeAsync() => transportChannel.DisposeAsync();

    public async ValueTask<PeerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        byte[] senderBuffer = new byte[1024];
        await transportChannel.ReceiveAsync(senderBuffer, ct);
        return JsonSerializer.Deserialize<PeerNetworkMessage>(
            senderBuffer,
            SignalingJsonContext.Default.PeerNetworkMessage
        );
    }

    public ValueTask SendMessageAsync(PeerNetworkMessage message, CancellationToken ct = default)
    {
        byte[] joinPayload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            SignalingJsonContext.Default.PeerNetworkMessage 
        );
        return transportChannel.SendAsync(joinPayload, ct);
    }

}

public sealed class ServerMessageChannel(ITransportChannel transportChannel) : IServerMessageChannel
{
    public bool IsConnected => transportChannel.IsConnected; // Assuming ITransportChannel has an IsConnected property

    public Task CloseAsync(CancellationToken ct = default) => transportChannel.CloseAsync(ct);

    public ValueTask DisposeAsync() => transportChannel.DisposeAsync();

    public async ValueTask<ServerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        byte[] senderBuffer = new byte[1024];
        await transportChannel.ReceiveAsync(senderBuffer, ct);
        return JsonSerializer.Deserialize<ServerNetworkMessage>(
            senderBuffer,
            SignalingJsonContext.Default.ServerNetworkMessage
        );
    }

    public ValueTask SendMessageAsync(ServerNetworkMessage message, CancellationToken ct = default)
    {
        byte[] joinPayload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            SignalingJsonContext.Default.ServerNetworkMessage 
        );
        return transportChannel.SendAsync(joinPayload, ct);
    }
}