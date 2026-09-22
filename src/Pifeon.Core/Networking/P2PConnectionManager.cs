using System.Net;
using System.Net.Sockets;

namespace Pifeon.Core.Networking;

public class P2PConnectionManager : IDisposable
{
    private Socket? _peerSocket;

    public bool IsConnected => _peerSocket != null && _peerSocket.Connected;

    /// <summary>
    /// Mette il mittente in ascolto su una porta per accettare la connessione diretta dal ricevitore.
    /// </summary>
    public async Task<bool> ListenForPeerAsync(int port, CancellationToken ct = default)
    {
        using var listener = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        listener.Bind(new IPEndPoint(IPAddress.Any, port));
        listener.Listen(1);

        using CancellationTokenRegistration registration = ct.Register(() => listener.Close());

        try
        {
            _peerSocket = await listener.AcceptAsync(ct);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Connette il ricevitore all'IP e alla porta del mittente.
    /// </summary>
    public async Task<bool> ConnectToPeerAsync(IPEndPoint remoteEndPoint, CancellationToken ct = default)
    {
        _peerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        try
        {
            await _peerSocket.ConnectAsync(remoteEndPoint, ct);
            return true;
        }
        catch
        {
            _peerSocket.Dispose();
            _peerSocket = null;
            return false;
        }
    }

    /// <summary>
    /// Invia un buffer di dati al peer con prefisso di lunghezza per evitare frammentazione TCP.
    /// </summary>
    public async Task SendBytesAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        if (_peerSocket == null || !_peerSocket.Connected)
        {
            throw new InvalidOperationException("Socket P2P non connesso.");
        }

        byte[] lengthPrefix = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, data.Length);

        await _peerSocket.SendAsync(lengthPrefix, SocketFlags.None, ct);
        await _peerSocket.SendAsync(data, SocketFlags.None, ct);
    }

    /// <summary>
    /// Riceve esattamente il pacchetto inviato dal peer.
    /// </summary>
    public async Task<byte[]> ReceiveBytesAsync(CancellationToken ct = default)
    {
        if (_peerSocket == null || !_peerSocket.Connected)
        {
            throw new InvalidOperationException("Socket P2P non connesso.");
        }

        byte[] lengthBuffer = new byte[4];
        await ReadExactAsync(_peerSocket, lengthBuffer, ct);

        int payloadLength = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(lengthBuffer);
        byte[] payloadBuffer = new byte[payloadLength];

        await ReadExactAsync(_peerSocket, payloadBuffer, ct);
        return payloadBuffer;
    }

    private static async Task ReadExactAsync(Socket socket, Memory<byte> buffer, CancellationToken ct)
    {
        int totalRead = 0;
        while (totalRead < buffer.Length)
        {
            int read = await socket.ReceiveAsync(buffer[totalRead..], SocketFlags.None, ct);
            if (read == 0)
            {
                throw new SocketException((int)SocketError.ConnectionReset);
            }

            totalRead += read;
        }
    }

    public void Dispose()
    {
        _peerSocket?.Dispose();
    }
}
