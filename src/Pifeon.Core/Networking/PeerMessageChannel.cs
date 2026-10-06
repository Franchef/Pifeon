using System.Buffers;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Networking;

public sealed class PeerMessageChannel(ITransportChannel transportChannel) : IPeerMessageChannel
{
    public const int MaximumChunkSize = 64 * 1024;
    private const int ChunkHeaderSize = 1 + sizeof(long);

    public bool IsConnected => transportChannel.IsConnected;

    public Task CloseAsync(CancellationToken ct = default) => transportChannel.CloseAsync(ct);

    public ValueTask DisposeAsync() => transportChannel.DisposeAsync();

    public async ValueTask<PeerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(TcpTransportChannel.MaximumMessageSize);
        try
        {
            int bytesRead = await transportChannel.ReceiveAsync(buffer.AsMemory(0, TcpTransportChannel.MaximumMessageSize), ct);
            if (bytesRead <= 0)
            {
                throw new InvalidOperationException("Peer channel closed while waiting for a message.");
            }

            if (buffer[0] == (byte)PeerNetworkMessageType.ChunkData)
            {
                if (bytesRead <= ChunkHeaderSize || bytesRead - ChunkHeaderSize > MaximumChunkSize)
                {
                    throw new InvalidDataException("Incomplete binary file chunk.");
                }
                return new PeerNetworkMessage(PeerNetworkMessageType.ChunkData,
                    BinaryPrimitives.ReadInt64BigEndian(buffer.AsSpan(1, sizeof(long))),
                    buffer.AsSpan(ChunkHeaderSize, bytesRead - ChunkHeaderSize).ToArray());
            }
            PeerNetworkMessage message = JsonSerializer.Deserialize(buffer.AsSpan(0, bytesRead), SignalingJsonContext.Default.PeerNetworkMessage);
            if (message.Type == PeerNetworkMessageType.ChunkData || !Enum.IsDefined(message.Type))
            {
                throw new InvalidDataException("File chunks must use binary framing; unknown peer message type.");
            }
            return message;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    public ValueTask SendMessageAsync(PeerNetworkMessage message, CancellationToken ct = default)
    {
        byte[] payload;
        if (message.Type == PeerNetworkMessageType.ChunkData)
        {
            if (message.Payload.Length is <= 0 or > MaximumChunkSize)
            {
                throw new InvalidDataException("File chunks must contain 1 to 65536 bytes.");
            }
            payload = new byte[checked(ChunkHeaderSize + message.Payload.Length)];
            payload[0] = (byte)message.Type;
            BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, sizeof(long)), message.SequenceNumber);
            message.Payload.CopyTo(payload.AsMemory(ChunkHeaderSize));
        }
        else
        {
            payload = JsonSerializer.SerializeToUtf8Bytes(message, SignalingJsonContext.Default.PeerNetworkMessage);
        }
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
        byte[] buffer = new byte[64 * 1024];
        int bytesRead = await transportChannel.ReceiveAsync(buffer, ct);

        if (bytesRead <= 0)
        {
            throw new InvalidOperationException("Server channel closed while waiting for a message.");
        }

        ReadOnlySpan<byte> payload = buffer.AsSpan(0, bytesRead);

        using JsonDocument document = JsonDocument.Parse(payload.ToArray());
        if (document.RootElement.TryGetProperty("type", out JsonElement type) && type.ValueKind == JsonValueKind.String)
        {
            return DeserializeLegacySignalingMessage(payload);
        }
        return JsonSerializer.Deserialize(payload, SignalingJsonContext.Default.ServerNetworkMessage);
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
            "IP_EXCHANGE" => ServerNetworkMessageType.IpExchanges,
            "ERROR" => ServerNetworkMessageType.Abort,
            _ => ServerNetworkMessageType.Handshake
        };

        return new ServerNetworkMessage(mappedType, 0, payload.ToArray());
    }
}
