using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Cryptography;

namespace Pifeon.Core.Networking;

public sealed class EncryptedTransportChannel : ITransportChannel
{
    private readonly ITransportChannel _transport;
    private readonly byte[] _sendKey;
    private readonly byte[] _receiveKey;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private ulong _sendCounter;
    private ulong _receiveCounter;
    private bool _disposed;

    public bool IsConnected => !_disposed && _transport.IsConnected;

    public EncryptedTransportChannel(ITransportChannel transport, ReadOnlySpan<byte> sharedSecret, string code, bool isSender)
    {
        _transport = transport;
        byte[] salt = Encoding.UTF8.GetBytes(code);
        byte[] senderKey = new byte[32];
        byte[] receiverKey = new byte[32];
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, senderKey, salt, "Pifeon-v1-sender"u8);
        HKDF.DeriveKey(HashAlgorithmName.SHA256, sharedSecret, receiverKey, salt, "Pifeon-v1-receiver"u8);
        _sendKey = isSender ? senderKey : receiverKey;
        _receiveKey = isSender ? receiverKey : senderKey;
    }

    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _sendLock.WaitAsync(ct);
        try
        {
            byte[] plaintext = new byte[checked(data.Length + 8)];
            BinaryPrimitives.WriteUInt64BigEndian(plaintext, _sendCounter);
            data.CopyTo(plaintext.AsMemory(8));
            byte[] encrypted = AesGcmEncryption.Encrypt(plaintext, _sendKey);
            await _transport.SendAsync(encrypted, ct);
            _sendCounter = checked(_sendCounter + 1);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        byte[] encrypted = ArrayPool<byte>.Shared.Rent(TcpTransportChannel.MaximumMessageSize);
        try
        {
            int length = await _transport.ReceiveAsync(encrypted.AsMemory(0, TcpTransportChannel.MaximumMessageSize), ct);
            byte[] plaintext = AesGcmEncryption.Decrypt(encrypted.AsSpan(0, length), _receiveKey);
            if (plaintext.Length < 8 || BinaryPrimitives.ReadUInt64BigEndian(plaintext) != _receiveCounter)
            {
                throw new CryptographicException("Peer packet is replayed or out of order.");
            }
            int payloadLength = plaintext.Length - 8;
            if (payloadLength > buffer.Length)
            {
                throw new InvalidDataException("Encrypted peer message exceeds the receive limit.");
            }

            plaintext.AsMemory(8).CopyTo(buffer);
            _receiveCounter = checked(_receiveCounter + 1);
            return payloadLength;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(encrypted, clearArray: true);
        }
    }

    public Task CloseAsync(CancellationToken ct = default) => _transport.CloseAsync(ct);

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        CryptographicOperations.ZeroMemory(_sendKey);
        CryptographicOperations.ZeroMemory(_receiveKey);
        await _transport.DisposeAsync();
    }
}
