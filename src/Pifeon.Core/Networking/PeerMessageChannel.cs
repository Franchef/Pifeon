using System.Text;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Networking;

public sealed class PeerMessageChannel(ITransportChannel transportChannel) : IPeerMessageChannel
{
    public bool IsConnected => transportChannel.IsConnected;

    public Task CloseAsync(CancellationToken ct = default) => transportChannel.CloseAsync(ct);

    public ValueTask DisposeAsync() => transportChannel.DisposeAsync();

    public async ValueTask<PeerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        byte[] buffer = new byte[16 * 1024];
        int bytesRead = await transportChannel.ReceiveAsync(buffer, ct);

        if (bytesRead <= 0)
        {
            throw new InvalidOperationException("Peer channel closed while waiting for a message.");
        }

        return JsonSerializer.Deserialize(
            buffer.AsSpan(0, bytesRead),
            SignalingJsonContext.Default.PeerNetworkMessage);
    }

    public ValueTask SendMessageAsync(PeerNetworkMessage message, CancellationToken ct = default)
    {
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            message,
            SignalingJsonContext.Default.PeerNetworkMessage
        );
        return transportChannel.SendAsync(payload, ct);
    }

}

public sealed class ServerMessageChannel(ITransportChannel transportChannel) : IServerMessageChannel
{
    public bool IsConnected => transportChannel.IsConnected;

    public Task CloseAsync(CancellationToken ct = default) => transportChannel.CloseAsync(ct);

    public ValueTask DisposeAsync() => transportChannel.DisposeAsync();

    public async ValueTask<ServerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        byte[] buffer = new byte[16 * 1024];
        int bytesRead = await transportChannel.ReceiveAsync(buffer, ct);

        if (bytesRead <= 0)
        {
            throw new InvalidOperationException("Server channel closed while waiting for a message.");
        }

        ReadOnlySpan<byte> payload = buffer.AsSpan(0, bytesRead);

        try
        {
            return JsonSerializer.Deserialize(payload, SignalingJsonContext.Default.ServerNetworkMessage);
        }
        catch (JsonException)
        {
            return DeserializeLegacySignalingMessage(payload);
        }
    }

    public ValueTask SendMessageAsync(ServerNetworkMessage message, CancellationToken ct = default)
    {
        byte[] payload =
            message.Payload.Length > 0 && message.Payload.Span[0] is (byte)'{' or (byte)'['
                ? message.Payload.ToArray()
                : message.Type == ServerNetworkMessageType.Close
                    ? Encoding.UTF8.GetBytes("{\"action\":\"CLOSE\"}")
                    : JsonSerializer.SerializeToUtf8Bytes(
                message,
                SignalingJsonContext.Default.ServerNetworkMessage
            );

        return transportChannel.SendAsync(payload, ct);
    }

    private static ServerNetworkMessage DeserializeLegacySignalingMessage(ReadOnlySpan<byte> payload)
    {
        using JsonDocument doc = JsonDocument.Parse(payload.ToArray());

        if (!doc.RootElement.TryGetProperty("type", out JsonElement typeProperty))
        {
            throw new JsonException("Unable to parse signaling message type.");
        }

        string type = typeProperty.GetString() ?? string.Empty;
        ServerNetworkMessageType mappedType = type switch
        {
            "CODE_CREATED" => ServerNetworkMessageType.Handshake,
            "RECEIVER_JOINED" => ServerNetworkMessageType.IpExchanges,
            "ERROR" => ServerNetworkMessageType.Abort,
            _ => ServerNetworkMessageType.Handshake
        };

        return new ServerNetworkMessage(mappedType, 0, payload.ToArray());
    }
}
