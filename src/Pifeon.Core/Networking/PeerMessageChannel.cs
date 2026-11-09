using System.Buffers;
using System.Buffers.Binary;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Networking;

internal sealed class PeerMessageChannel(ITransportChannel transportChannel) : IPeerMessageChannel
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

    public async ValueTask SendMessageAsync(PeerNetworkMessage message, CancellationToken ct = default)
    {
        if (message.Type == PeerNetworkMessageType.ChunkData)
        {
            if (message.Payload.Length is <= 0 or > MaximumChunkSize)
            {
                throw new InvalidDataException("File chunks must contain 1 to 65536 bytes.");
            }
            int payloadLength = checked(ChunkHeaderSize + message.Payload.Length);
            byte[] payload = ArrayPool<byte>.Shared.Rent(payloadLength);
            try
            {
                payload[0] = (byte)message.Type;
                BinaryPrimitives.WriteInt64BigEndian(payload.AsSpan(1, sizeof(long)), message.SequenceNumber);
                message.Payload.CopyTo(payload.AsMemory(ChunkHeaderSize));
                await transportChannel.SendAsync(payload.AsMemory(0, payloadLength), ct);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(payload, clearArray: true);
            }

            return;
        }
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(message, SignalingJsonContext.Default.PeerNetworkMessage);
        await transportChannel.SendAsync(json, ct);
    }
}
