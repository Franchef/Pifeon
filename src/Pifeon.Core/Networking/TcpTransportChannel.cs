using System.Buffers.Binary;
using System.Net.Sockets;
using Pifeon.Core.Abstractions;

namespace Pifeon.Core.Networking;

public sealed class TcpTransportChannel(TcpClient client) : ITransportChannel
{
    public const int MaximumMessageSize = 4 * 1024 * 1024;
    private readonly NetworkStream _stream = client.GetStream();
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private bool _disposed;

    public bool IsConnected => !_disposed && client.Connected;

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (data.Length is <= 0 or > MaximumMessageSize)
        {
            throw new InvalidDataException("Invalid TCP message length.");
        }

        await _sendLock.WaitAsync(ct);
        try
        {
            byte[] prefix = new byte[4];
            BinaryPrimitives.WriteInt32BigEndian(prefix, data.Length);
            await _stream.WriteAsync(prefix, ct);
            await _stream.WriteAsync(data, ct);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] prefix = new byte[4];
        await _stream.ReadExactlyAsync(prefix, ct);
        int length = BinaryPrimitives.ReadInt32BigEndian(prefix);
        if (length <= 0 || length > MaximumMessageSize || length > buffer.Length)
        {
            throw new InvalidDataException("TCP message exceeds the receive limit.");
        }

        await _stream.ReadExactlyAsync(buffer[..length], ct);
        return length;
    }

    public Task CloseAsync(CancellationToken ct = default)
    {
        client.Dispose();
        _disposed = true;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
    }
}
