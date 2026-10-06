using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Cryptography;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class SecurePeerTransportTests
{
    [Fact]
    public async Task FileChunk_ShouldUseBinaryHeaderAndUnchangedBytes()
    {
        var transport = new RecordingTransport();
        var channel = new PeerMessageChannel(transport);
        byte[] bytes = RandomNumberGenerator.GetBytes(64 * 1024);

        await channel.SendMessageAsync(new PeerNetworkMessage(PeerNetworkMessageType.ChunkData, 17, bytes), TestContext.Current.CancellationToken);

        byte[] packet = Assert.Single(transport.Sent);
        Assert.Equal(bytes.Length + 9, packet.Length);
        Assert.Equal((byte)PeerNetworkMessageType.ChunkData, packet[0]);
        Assert.Equal(17, BinaryPrimitives.ReadInt64BigEndian(packet.AsSpan(1, 8)));
        Assert.Equal(bytes, packet.AsSpan(9).ToArray());

        transport.Incoming.Enqueue(packet);
        PeerNetworkMessage received = await channel.ReceiveMessageAsync(TestContext.Current.CancellationToken);
        Assert.Equal(bytes, received.Payload.ToArray());
        Assert.Equal(17, received.SequenceNumber);
    }

    [Fact]
    public async Task ManifestLargerThan16KiB_ShouldRemainJsonAndRoundTrip()
    {
        var transport = new RecordingTransport();
        var channel = new PeerMessageChannel(transport);
        var manifest = new TransferManifest(512, 0, Enumerable.Range(0, 512)
            .Select(i => new TransferItemInfo($"{new string('a', 100)}-{i}.txt", 0)).ToArray());
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(manifest, SignalingJsonContext.Default.TransferManifest);
        Assert.True(payload.Length > 16 * 1024);
        await channel.SendMessageAsync(new PeerNetworkMessage(PeerNetworkMessageType.Manifest, 0, payload), TestContext.Current.CancellationToken);

        byte[] packet = Assert.Single(transport.Sent);
        using JsonDocument json = JsonDocument.Parse(packet);
        transport.Incoming.Enqueue(packet);
        PeerNetworkMessage received = await channel.ReceiveMessageAsync(TestContext.Current.CancellationToken);
        Assert.Equal(payload, received.Payload.ToArray());
    }

    [Fact]
    public async Task JsonSerializedFileChunk_ShouldBeRejected()
    {
        var transport = new RecordingTransport();
        transport.Incoming.Enqueue(JsonSerializer.SerializeToUtf8Bytes(
            new PeerNetworkMessage(PeerNetworkMessageType.ChunkData, 1, "bytes"u8.ToArray()),
            SignalingJsonContext.Default.PeerNetworkMessage));
        var channel = new PeerMessageChannel(transport);
        await Assert.ThrowsAsync<InvalidDataException>(() => channel.ReceiveMessageAsync(TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task EcdhAndEncryption_ShouldRoundTripInBothDirections()
    {
        using var senderExchange = new KeyExchange();
        using var receiverExchange = new KeyExchange();
        byte[] senderSecret = senderExchange.DeriveSharedSecret(receiverExchange.GetPublicKey());
        byte[] receiverSecret = receiverExchange.DeriveSharedSecret(senderExchange.GetPublicKey());
        var senderWire = new RecordingTransport();
        var receiverWire = new RecordingTransport();
        await using var sender = new EncryptedTransportChannel(senderWire, senderSecret, "123456", true);
        await using var receiver = new EncryptedTransportChannel(receiverWire, receiverSecret, "123456", false);
        CryptographicOperations.ZeroMemory(senderSecret);
        CryptographicOperations.ZeroMemory(receiverSecret);
        byte[] bytes = RandomNumberGenerator.GetBytes(64 * 1024);

        await sender.SendAsync(bytes, TestContext.Current.CancellationToken);
        byte[] ciphertext = Assert.Single(senderWire.Sent);
        Assert.Equal(bytes.Length + 8 + AesGcmEncryption.NonceSize + AesGcmEncryption.TagSize, ciphertext.Length);
        receiverWire.Incoming.Enqueue(ciphertext);
        byte[] received = new byte[bytes.Length];
        Assert.Equal(bytes.Length, await receiver.ReceiveAsync(received, TestContext.Current.CancellationToken));
        Assert.Equal(bytes, received);

        await receiver.SendAsync("ack"u8.ToArray(), TestContext.Current.CancellationToken);
        senderWire.Incoming.Enqueue(Assert.Single(receiverWire.Sent));
        Assert.Equal(3, await sender.ReceiveAsync(received, TestContext.Current.CancellationToken));
        Assert.Equal("ack"u8.ToArray(), received.AsSpan(0, 3).ToArray());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12)]
    [InlineData(28)]
    public async Task Encryption_ShouldRejectTamperedNonceTagOrCiphertext(int offset)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        var senderWire = new RecordingTransport();
        var receiverWire = new RecordingTransport();
        await using var sender = new EncryptedTransportChannel(senderWire, secret, "123456", true);
        await using var receiver = new EncryptedTransportChannel(receiverWire, secret, "123456", false);
        await sender.SendAsync("file-data"u8.ToArray(), TestContext.Current.CancellationToken);
        byte[] tampered = Assert.Single(senderWire.Sent).ToArray();
        tampered[offset] ^= 1;
        receiverWire.Incoming.Enqueue(tampered);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => receiver.ReceiveAsync(new byte[100], TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task Encryption_ShouldRejectReplayedPacket()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        var senderWire = new RecordingTransport();
        var receiverWire = new RecordingTransport();
        await using var sender = new EncryptedTransportChannel(senderWire, secret, "123456", true);
        await using var receiver = new EncryptedTransportChannel(receiverWire, secret, "123456", false);
        await sender.SendAsync("file-data"u8.ToArray(), TestContext.Current.CancellationToken);
        byte[] packet = Assert.Single(senderWire.Sent);
        receiverWire.Incoming.Enqueue(packet);
        receiverWire.Incoming.Enqueue(packet);
        await receiver.ReceiveAsync(new byte[100], TestContext.Current.CancellationToken);
        await Assert.ThrowsAsync<CryptographicException>(() => receiver.ReceiveAsync(new byte[100], TestContext.Current.CancellationToken).AsTask());
    }

    [Theory]
    [InlineData("654321", false)]
    [InlineData("123456", true)]
    public async Task Encryption_ShouldBindSessionAndDirection(string code, bool isSender)
    {
        byte[] secret = RandomNumberGenerator.GetBytes(32);
        var senderWire = new RecordingTransport();
        var receiverWire = new RecordingTransport();
        await using var sender = new EncryptedTransportChannel(senderWire, secret, "123456", true);
        await using var receiver = new EncryptedTransportChannel(receiverWire, secret, code, isSender);
        await sender.SendAsync("file-data"u8.ToArray(), TestContext.Current.CancellationToken);
        receiverWire.Incoming.Enqueue(Assert.Single(senderWire.Sent));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => receiver.ReceiveAsync(new byte[100], TestContext.Current.CancellationToken).AsTask());
    }

    [Fact]
    public async Task TcpTransport_ShouldAssembleFragmentsAndSeparateMessages()
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(10));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var writer = new TcpClient();
        await writer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cts.Token);
        await using var reader = new TcpTransportChannel(await listener.AcceptTcpClientAsync(cts.Token));
        byte[] bytes = RandomNumberGenerator.GetBytes(64 * 1024);
        byte[] packet = new byte[bytes.Length + 4];
        BinaryPrimitives.WriteInt32BigEndian(packet, bytes.Length);
        bytes.CopyTo(packet, 4);

        Task sendTask = Task.Run(async () =>
        {
            for (int offset = 0; offset < packet.Length; offset += 257)
            {
                await writer.GetStream().WriteAsync(packet.AsMemory(offset, Math.Min(257, packet.Length - offset)), cts.Token);
            }
            await writer.GetStream().WriteAsync(packet, cts.Token);
        }, cts.Token);
        byte[] buffer = new byte[bytes.Length];
        Assert.Equal(bytes.Length, await reader.ReceiveAsync(buffer, cts.Token));
        Assert.Equal(bytes, buffer);
        Assert.Equal(bytes.Length, await reader.ReceiveAsync(buffer, cts.Token));
        Assert.Equal(bytes, buffer);
        await sendTask;
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(TcpTransportChannel.MaximumMessageSize + 1)]
    public async Task TcpTransport_ShouldRejectInvalidLengthBeforeReadingPayload(int length)
    {
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var writer = new TcpClient();
        await writer.ConnectAsync((IPEndPoint)listener.LocalEndpoint, cts.Token);
        await using var reader = new TcpTransportChannel(await listener.AcceptTcpClientAsync(cts.Token));
        byte[] prefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(prefix, length);
        await writer.GetStream().WriteAsync(prefix, cts.Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => reader.ReceiveAsync(new byte[100], cts.Token).AsTask());
    }

    [Fact]
    public async Task DirectConnection_ShouldReportUnreachableEndpoints()
    {
        using var exchange = new KeyExchange();
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        var signalingWire = new RecordingTransport();
        signalingWire.Incoming.Enqueue(JsonSerializer.SerializeToUtf8Bytes(new IpExchange
        {
            IsSender = true,
            LocalIps = [IPAddress.Loopback.ToString()],
            Port = port,
            PublicKey = exchange.GetPublicKey()
        }, SignalingJsonContext.Default.IpExchange));
        await using var direct = new DirectPeerMessageChannel();
        using CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(5));
        IOException error = await Assert.ThrowsAsync<IOException>(() => direct.EstablishAsync(new ServerMessageChannel(signalingWire), "123456", false, cts.Token));
        Assert.Contains("No reachable direct TCP endpoint", error.Message);
        Assert.False(direct.IsConnected);
    }

    [Fact]
    public void KeyExchange_ShouldRejectNonP256AndTrailingData()
    {
        using var exchange = new KeyExchange();
        using ECDiffieHellman wrongCurve = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP384);
        Assert.Throws<CryptographicException>(() => exchange.DeriveSharedSecret(wrongCurve.ExportSubjectPublicKeyInfo()));
        Assert.Throws<CryptographicException>(() => exchange.DeriveSharedSecret([.. exchange.GetPublicKey(), 0]));
    }

    private sealed class RecordingTransport : ITransportChannel
    {
        public List<byte[]> Sent { get; } = [];
        public Queue<byte[]> Incoming { get; } = [];
        public bool IsConnected { get; private set; } = true;

        public ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Sent.Add(data.ToArray());
            return ValueTask.CompletedTask;
        }

        public ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            byte[] packet = Incoming.Dequeue();
            packet.CopyTo(buffer);
            return ValueTask.FromResult(packet.Length);
        }

        public Task CloseAsync(CancellationToken ct = default)
        {
            IsConnected = false;
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync()
        {
            IsConnected = false;
            return ValueTask.CompletedTask;
        }
    }
}
