using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Cryptography;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Networking;

public sealed class DirectPeerMessageChannel : IPeerMessageChannel
{
    private PeerMessageChannel? _channel;
    private bool _ready;
    private bool _disposed;
    private int _establishmentStarted;

    public bool IsConnected => _ready && _channel?.IsConnected == true;

    public async Task EstablishAsync(IServerMessageChannel signaling, string code, bool isSender, CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _establishmentStarted, 1) != 0)
        {
            throw new InvalidOperationException("Peer connection establishment has already started.");
        }
        using var keyExchange = new KeyExchange();
        using var listener = new TcpListener(IPAddress.Any, 0);
        if (isSender)
        {
            listener.Start();
        }

        ct.ThrowIfCancellationRequested();
        IPAddress[] addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .OrderBy(IPAddress.IsLoopback).ToArray();
        var local = new IpExchange
        {
            IsSender = isSender,
            LocalIps = addresses.Select(a => a.ToString()).Append(IPAddress.Loopback.ToString()).Distinct().ToList(),
            Port = isSender ? ((IPEndPoint)listener.LocalEndpoint).Port : 0,
            PublicKey = keyExchange.GetPublicKey()
        };
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(local, SignalingJsonContext.Default.IpExchange);
        await signaling.SendMessageAsync(new ServerNetworkMessage(ServerNetworkMessageType.IpExchanges, 0, json), ct);

        IpExchange remote;
        while (true)
        {
            ServerNetworkMessage message = await signaling.ReceiveMessageAsync(ct);
            using JsonDocument document = JsonDocument.Parse(message.Payload);
            string? type = document.RootElement.GetProperty("type").GetString();
            if (type == "RECEIVER_JOINED")
            {
                continue;
            }
            if (type != "IP_EXCHANGE")
            {
                throw new InvalidDataException($"Unexpected signaling message: {type}.");
            }
            remote = JsonSerializer.Deserialize(message.Payload.Span, SignalingJsonContext.Default.IpExchange)
                ?? throw new InvalidDataException("Missing peer endpoint.");
            break;
        }

        if (remote.IsSender == isSender || remote.PublicKey is null || remote.PublicKey.Length == 0
            || remote.LocalIps is null || remote.LocalIps.Count is 0 or > 32)
        {
            throw new InvalidDataException("Invalid peer endpoint or key exchange.");
        }

        using CancellationTokenSource connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectionCts.CancelAfter(TimeSpan.FromSeconds(30));
        byte[] sharedSecret = keyExchange.DeriveSharedSecret(remote.PublicKey);
        try
        {
            TcpClient client = isSender
                ? await listener.AcceptTcpClientAsync(connectionCts.Token)
                : await ConnectAsync(remote, connectionCts.Token);
            client.NoDelay = true;
            _channel = new PeerMessageChannel(new EncryptedTransportChannel(new TcpTransportChannel(client), sharedSecret, code, isSender));
            byte[] confirmation = isSender ? "sender-ready"u8.ToArray() : "receiver-ready"u8.ToArray();
            await _channel.SendMessageAsync(new PeerNetworkMessage(PeerNetworkMessageType.Handshake, 0, confirmation), connectionCts.Token);
            PeerNetworkMessage proof = await _channel.ReceiveMessageAsync(connectionCts.Token);
            ReadOnlyMemory<byte> expected = isSender ? "receiver-ready"u8.ToArray() : "sender-ready"u8.ToArray();
            if (proof.Type != PeerNetworkMessageType.Handshake || !proof.Payload.Span.SequenceEqual(expected.Span))
            {
                throw new CryptographicException("Peer key confirmation failed.");
            }
            _ready = true;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    private static async Task<TcpClient> ConnectAsync(IpExchange remote, CancellationToken ct)
    {
        if (remote.Port is <= 0 or > 65535)
        {
            throw new InvalidDataException("Invalid peer TCP port.");
        }
        var failures = new List<Exception>();
        foreach (string address in remote.LocalIps)
        {
            if (!IPAddress.TryParse(address, out IPAddress? ip) || ip.AddressFamily != AddressFamily.InterNetwork)
            {
                throw new InvalidDataException("Invalid peer IPv4 address.");
            }
            var client = new TcpClient(AddressFamily.InterNetwork);
            using CancellationTokenSource attemptCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            attemptCts.CancelAfter(TimeSpan.FromSeconds(3));
            try
            {
                await client.ConnectAsync(ip, remote.Port, attemptCts.Token);
                return client;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                client.Dispose();
                ct.ThrowIfCancellationRequested();
                failures.Add(ex);
            }
        }
        throw new IOException("No reachable direct TCP endpoint. Check the sender firewall and network routing.", new AggregateException(failures));
    }

    public ValueTask SendMessageAsync(PeerNetworkMessage message, CancellationToken ct = default) =>
        ConnectedChannel.SendMessageAsync(message, ct);

    public ValueTask<PeerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default) =>
        ConnectedChannel.ReceiveMessageAsync(ct);

    private PeerMessageChannel ConnectedChannel => _ready && _channel is not null
        ? _channel
        : throw new InvalidOperationException("Direct peer connection is not established.");

    public Task CloseAsync(CancellationToken ct = default)
    {
        _ready = false;
        return _channel?.CloseAsync(ct) ?? Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _ready = false;
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
    }
}
