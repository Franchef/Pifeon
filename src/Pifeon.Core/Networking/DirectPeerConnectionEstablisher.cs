using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Cryptography;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Networking;

internal static class DirectPeerConnectionEstablisher
{
    private static readonly byte[] SenderConfirmation = "sender-ready"u8.ToArray();
    private static readonly byte[] ReceiverConfirmation = "receiver-ready"u8.ToArray();

    public static async Task<PeerMessageChannel> EstablishAsync(
        IServerMessageChannel signaling,
        string code,
        PeerRole role,
        CancellationToken ct)
    {
        bool isSender = role == PeerRole.Sender;
        using var keyExchange = new KeyExchange();
        using var listener = new TcpListener(IPAddress.Any, 0);
        if (isSender)
        {
            listener.Start();
        }

        IpExchange local = CreateLocalEndpoint(keyExchange, listener, isSender);
        byte[] announcement = JsonSerializer.SerializeToUtf8Bytes(local, SignalingJsonContext.Default.IpExchange);
        await signaling.SendMessageAsync(
            new ServerNetworkMessage(ServerNetworkMessageType.IpExchanges, 0, announcement),
            ct);

        IpExchange remote = await ReceiveRemoteEndpointAsync(signaling, isSender, ct);
        using CancellationTokenSource connectionCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        connectionCts.CancelAfter(TimeSpan.FromSeconds(30));

        byte[] sharedSecret = keyExchange.DeriveSharedSecret(remote.PublicKey!);
        PeerMessageChannel? channel = null;
        TcpClient? client = null;
        try
        {
            client = isSender
                ? await listener.AcceptTcpClientAsync(connectionCts.Token)
                : await ConnectAsync(remote, connectionCts.Token);
            client.NoDelay = true;

            channel = new PeerMessageChannel(
                new EncryptedTransportChannel(
                    new TcpTransportChannel(client),
                    sharedSecret,
                    code,
                    isSender));
            client = null;

            await ConfirmKeysAsync(channel, isSender, connectionCts.Token);
            return channel;
        }
        catch
        {
            if (channel is not null)
            {
                await channel.DisposeAsync();
            }
            else
            {
                client?.Dispose();
            }

            throw;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    private static IpExchange CreateLocalEndpoint(KeyExchange keyExchange, TcpListener listener, bool isSender)
    {
        string[] addresses = NetworkInterface.GetAllNetworkInterfaces()
            .Where(network => network.OperationalStatus == OperationalStatus.Up)
            .SelectMany(network => network.GetIPProperties().UnicastAddresses)
            .Select(address => address.Address)
            .Where(address => address.AddressFamily == AddressFamily.InterNetwork)
            .OrderBy(IPAddress.IsLoopback)
            .Select(address => address.ToString())
            .Append(IPAddress.Loopback.ToString())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return new IpExchange
        {
            IsSender = isSender,
            LocalIps = [.. addresses],
            Port = isSender ? ((IPEndPoint)listener.LocalEndpoint).Port : 0,
            PublicKey = keyExchange.GetPublicKey()
        };
    }

    private static async Task<IpExchange> ReceiveRemoteEndpointAsync(
        IServerMessageChannel signaling,
        bool isSender,
        CancellationToken ct)
    {
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

            IpExchange remote = JsonSerializer.Deserialize(
                message.Payload.Span,
                SignalingJsonContext.Default.IpExchange)
                ?? throw new InvalidDataException("Missing peer endpoint.");
            ValidateRemoteEndpoint(remote, isSender);
            return remote;
        }
    }

    private static void ValidateRemoteEndpoint(IpExchange remote, bool isSender)
    {
        if (remote.IsSender == isSender
            || remote.PublicKey is null
            || remote.PublicKey.Length == 0
            || remote.LocalIps is null
            || remote.LocalIps.Count is 0 or > 32)
        {
            throw new InvalidDataException("Invalid peer endpoint or key exchange.");
        }
    }

    private static async Task ConfirmKeysAsync(
        PeerMessageChannel channel,
        bool isSender,
        CancellationToken ct)
    {
        ReadOnlyMemory<byte> confirmation = isSender ? SenderConfirmation : ReceiverConfirmation;
        ReadOnlyMemory<byte> expected = isSender ? ReceiverConfirmation : SenderConfirmation;

        await channel.SendMessageAsync(
            new PeerNetworkMessage(PeerNetworkMessageType.Handshake, 0, confirmation),
            ct);
        PeerNetworkMessage proof = await channel.ReceiveMessageAsync(ct);
        if (proof.Type != PeerNetworkMessageType.Handshake
            || !proof.Payload.Span.SequenceEqual(expected.Span))
        {
            throw new CryptographicException("Peer key confirmation failed.");
        }
    }

    private static async Task<TcpClient> ConnectAsync(IpExchange remote, CancellationToken ct)
    {
        if (remote.Port is <= 0 or > 65535)
        {
            throw new InvalidDataException("Invalid peer TCP port.");
        }

        var failures = new List<Exception>(remote.LocalIps!.Count);
        foreach (string address in remote.LocalIps)
        {
            if (!IPAddress.TryParse(address, out IPAddress? ip)
                || ip.AddressFamily != AddressFamily.InterNetwork)
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

        throw new IOException(
            "No reachable direct TCP endpoint. Check the sender firewall and network routing.",
            new AggregateException(failures));
    }
}
