using System.Buffers;
using System.Text.Json;
using Pifeon.Core.Abstractions;

namespace Pifeon.Core.Networking;

internal sealed class ServerMessageChannel(ITransportChannel transportChannel) : IServerMessageChannel
{
    private const int MaximumSignalingMessageSize = 64 * 1024;
    private static readonly byte[] ClosePayload = "{\"action\":\"CLOSE\"}"u8.ToArray();

    public bool IsConnected => transportChannel.IsConnected;

    public Task CloseAsync(CancellationToken ct = default) => transportChannel.CloseAsync(ct);

    public ValueTask DisposeAsync() => transportChannel.DisposeAsync();

    public async ValueTask<ServerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(MaximumSignalingMessageSize);
        try
        {
            int bytesRead = await transportChannel.ReceiveAsync(buffer.AsMemory(0, MaximumSignalingMessageSize), ct);
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("Server channel closed while waiting for a message.");
            }

            ReadOnlyMemory<byte> payload = buffer.AsMemory(0, bytesRead);
            using JsonDocument document = JsonDocument.Parse(payload);
            if (!document.RootElement.TryGetProperty("type", out JsonElement typeProperty)
                || typeProperty.ValueKind != JsonValueKind.String)
            {
                throw new JsonException("Unable to parse signaling message type.");
            }

            ServerNetworkMessageType type = typeProperty.GetString() switch
            {
                "CODE_CREATED" => ServerNetworkMessageType.Handshake,
                "RECEIVER_JOINED" or "IP_EXCHANGE" => ServerNetworkMessageType.IpExchanges,
                "ERROR" => ServerNetworkMessageType.Abort,
                _ => throw new JsonException("Unknown signaling message type.")
            };

            return new ServerNetworkMessage(type, 0, payload.ToArray());
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    public ValueTask SendMessageAsync(ServerNetworkMessage message, CancellationToken ct = default)
    {
        ReadOnlyMemory<byte> payload = message.Type == ServerNetworkMessageType.Close
            ? ClosePayload
            : message.Payload;

        if (payload.IsEmpty || payload.Span[0] is not ((byte)'{' or (byte)'['))
        {
            throw new InvalidDataException("Signaling messages must contain a JSON payload.");
        }

        return transportChannel.SendAsync(payload, ct);
    }
}
