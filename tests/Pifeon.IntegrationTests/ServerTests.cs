using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Cryptography;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.IntegrationTests;

public class SignalingIntegrationTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _factory;
    private const int DefaultTimeoutMs = 10000; // 10 seconds for WebSocket operations

    public SignalingIntegrationTests(ServerFixture factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a linked CancellationTokenSource that combines TestContext cancellation with a timeout.
    /// This prevents individual WebSocket operations from hanging indefinitely.
    /// </summary>
    private static CancellationTokenSource CreateTimeoutCancellationTokenSource(int timeoutMs = DefaultTimeoutMs)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeoutMs);
        return cts;
    }

    [Fact]
    public async Task FullPairingFlow_SenderAndReceiver_ShouldConnectSuccessfully()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(FullPairingFlow_SenderAndReceiver_ShouldConnectSuccessfully);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);

        string senderNotificationJson = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        Assert.Contains("RECEIVER_JOINED", senderNotificationJson);

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);
    }

    [Fact]
    public async Task ReceiverTryJoinWithWrongCode_ShouldFailWith404()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/join/000000"), cts.Token));

        Assert.Contains("status code: 404", exception.Message);
    }

    [Fact]
    public async Task EndpointAnnouncementBeforeJoin_ShouldBufferAndRelayBothPeerPublicKeys()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(EndpointAnnouncementBeforeJoin_ShouldBufferAndRelayBothPeerPublicKeys);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using var senderKey = new KeyExchange();
        using var receiverKey = new KeyExchange();
        var senderEndpoint = new IpExchange
        {
            IsSender = true,
            LocalIps = ["192.168.1.42"],
            Port = 53321,
            PublicKey = senderKey.GetPublicKey()
        };
        byte[] announcement = JsonSerializer.SerializeToUtf8Bytes(senderEndpoint, SignalingJsonContext.Default.IpExchange);
        await senderSocket.SendAsync(announcement, WebSocketMessageType.Text, true, cts.Token);
        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);

        _ = await ReceiveTextMessageAsync(senderSocket, cts.Token); // RECEIVER_JOINED
        IpExchange? receivedSender = JsonSerializer.Deserialize(
            await ReceiveTextMessageAsync(receiverSocket, cts.Token), SignalingJsonContext.Default.IpExchange);
        Assert.NotNull(receivedSender);
        Assert.True(receivedSender.IsSender);
        Assert.Equal(senderEndpoint.LocalIps, receivedSender.LocalIps);
        Assert.Equal(senderEndpoint.Port, receivedSender.Port);
        Assert.Equal(senderEndpoint.PublicKey, receivedSender.PublicKey);

        var receiverEndpoint = new IpExchange
        {
            IsSender = false,
            LocalIps = ["192.168.1.43"],
            PublicKey = receiverKey.GetPublicKey()
        };
        await receiverSocket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(receiverEndpoint,
            SignalingJsonContext.Default.IpExchange), WebSocketMessageType.Text, true, cts.Token);
        IpExchange? receivedReceiver = JsonSerializer.Deserialize(
            await ReceiveTextMessageAsync(senderSocket, cts.Token), SignalingJsonContext.Default.IpExchange);
        Assert.NotNull(receivedReceiver);
        Assert.False(receivedReceiver.IsSender);
        Assert.Equal(0, receivedReceiver.Port);
        Assert.Equal(receiverEndpoint.LocalIps, receivedReceiver.LocalIps);
        Assert.Equal(receiverEndpoint.PublicKey, receivedReceiver.PublicKey);

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(17)]
    public async Task FragmentedEndpointAnnouncement_ShouldRelayCompleteJsonAndCloseGracefully(int fragmentSize)
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(FragmentedEndpointAnnouncement_ShouldRelayCompleteJsonAndCloseGracefully);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);
        _ = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        using var key = new KeyExchange();
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(new IpExchange
        {
            IsSender = true,
            LocalIps = ["10.0.0.8"],
            Port = 6000,
            PublicKey = key.GetPublicKey()
        }, SignalingJsonContext.Default.IpExchange);
        for (int offset = 0; offset < payload.Length; offset += fragmentSize)
        {
            int length = Math.Min(fragmentSize, payload.Length - offset);
            await senderSocket.SendAsync(payload.AsMemory(offset, length), WebSocketMessageType.Text,
                offset + length == payload.Length, cts.Token);
        }
        string relayed = await ReceiveTextMessageAsync(receiverSocket, cts.Token);
        Assert.Equal(payload, Encoding.UTF8.GetBytes(relayed));

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);

        Assert.True(senderSocket.State is WebSocketState.CloseSent or WebSocketState.Closed);
        Assert.True(receiverSocket.State is WebSocketState.CloseSent or WebSocketState.Closed);
    }

    [Fact]
    public async Task BothClientsJoinSession_AndExchangeRawFileBytes_ShouldRejectNonJsonSignaling()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(BothClientsJoinSession_AndExchangeRawFileBytes_ShouldRejectNonJsonSignaling);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);
        _ = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        byte[] payload = [.. Enumerable.Range(1, 128).Select(i => (byte)i)];
        await senderSocket.SendAsync(payload, WebSocketMessageType.Binary, true, cts.Token);

        byte[] buffer = new byte[1024];
        WebSocketReceiveResult result = await senderSocket.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Close, result.MessageType);
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, result.CloseStatus);

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);
    }

    [Fact]
    public async Task SignalingError_ShouldRelayPeerConnectionFailure()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(SignalingError_ShouldRelayPeerConnectionFailure);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);
        _ = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        string abortPayload = JsonSerializer.Serialize(new ErrorResponse("ERROR", "peer cannot connect directly"), SignalingJsonContext.Default.ErrorResponse);
        await receiverSocket.SendAsync(Encoding.UTF8.GetBytes(abortPayload), WebSocketMessageType.Text, true, cts.Token);

        string relayed = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        Assert.Contains("ERROR", relayed);
        Assert.Contains("cannot connect", relayed);

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);
    }

    [Fact]
    public async Task Signaling_ShouldRejectMessagesLargerThan64KiB()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        using WebSocket sender = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse created = await ReadCodeCreatedAsync(sender, cts.Token);
        using WebSocket receiver = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{created.Code}"), cts.Token);
        _ = await ReceiveTextMessageAsync(sender, cts.Token);
        byte[] oversized = JsonSerializer.SerializeToUtf8Bytes(
            new ErrorResponse("ERROR", new string('x', 65536)), SignalingJsonContext.Default.ErrorResponse);

        await sender.SendAsync(oversized, WebSocketMessageType.Text, true, cts.Token);

        byte[] buffer = new byte[1024];
        WebSocketReceiveResult close = await sender.ReceiveAsync(buffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.InvalidPayloadData, close.CloseStatus);
        await CloseSocketSafeAsync(sender, cts.Token);
        await CloseSocketSafeAsync(receiver, cts.Token);
    }

    private static async Task<CodeCreatedResponse> ReadCodeCreatedAsync(WebSocket socket, CancellationToken ct)
    {
        string responseJson = await ReceiveTextMessageAsync(socket, ct);
        CodeCreatedResponse? codeCreatedResponse = JsonSerializer.Deserialize(responseJson, SignalingJsonContext.Default.CodeCreatedResponse);

        Assert.NotNull(codeCreatedResponse);
        Assert.Equal("CODE_CREATED", codeCreatedResponse.Type);
        Assert.False(string.IsNullOrWhiteSpace(codeCreatedResponse.Code));
        return codeCreatedResponse;
    }

    private static async Task<string> ReceiveTextMessageAsync(WebSocket socket, CancellationToken ct)
    {
        byte[] buffer = new byte[64 * 1024];
        int total = 0;
        WebSocketReceiveResult result;
        do
        {
            Assert.True(total < buffer.Length, "Signaling message exceeded the test receive limit.");
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer, total, buffer.Length - total), ct);
            Assert.Equal(WebSocketMessageType.Text, result.MessageType);
            total += result.Count;
        }
        while (!result.EndOfMessage);
        return Encoding.UTF8.GetString(buffer, 0, total);
    }

    private static async Task CloseSocketSafeAsync(WebSocket socket, CancellationToken ct)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test-end", ct);
        }
    }
}

public class SessionExpirationIntegrationTests : IClassFixture<ShortSessionTimeoutServerFixture>
{
    private readonly ShortSessionTimeoutServerFixture _factory;
    private const int DefaultTimeoutMs = 10000; // 10 seconds for WebSocket operations

    public SessionExpirationIntegrationTests(ShortSessionTimeoutServerFixture factory)
    {
        _factory = factory;
    }

    /// <summary>
    /// Creates a linked CancellationTokenSource that combines TestContext cancellation with a timeout.
    /// This prevents individual WebSocket operations from hanging indefinitely.
    /// </summary>
    private static CancellationTokenSource CreateTimeoutCancellationTokenSource(int timeoutMs = DefaultTimeoutMs)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeoutMs);
        return cts;
    }

    [Fact]
    public async Task CreateSession_AndExpire_ShouldFailJoinWith404()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreated = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        IPairingManager<WebSocket> manager = _factory.Services.GetRequiredService<IPairingManager<WebSocket>>();
        while (manager.TryGetSession(codeCreated.Code, out _))
        {
            await Task.Delay(10, cts.Token);
        }

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreated.Code}"), cts.Token));

        Assert.Contains("status code: 404", exception.Message);

        byte[] closeBuffer = new byte[1024];
        WebSocketReceiveResult close = await senderSocket.ReceiveAsync(closeBuffer, cts.Token);
        Assert.Equal(WebSocketMessageType.Close, close.MessageType);
        Assert.Equal(WebSocketCloseStatus.NormalClosure, close.CloseStatus);
        await senderSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test-end", cts.Token);
    }

    private static async Task<CodeCreatedResponse> ReadCodeCreatedAsync(WebSocket socket, CancellationToken ct)
    {
        byte[] buffer = new byte[16 * 1024];
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, ct);

        string responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
        CodeCreatedResponse? codeCreatedResponse = JsonSerializer.Deserialize(responseJson, SignalingJsonContext.Default.CodeCreatedResponse);

        Assert.NotNull(codeCreatedResponse);
        return codeCreatedResponse;
    }
}

public class RateLimitIntegrationTests : IClassFixture<ProductionRateLimitServerFixture>
{
    private readonly ProductionRateLimitServerFixture _factory;
    private const int DefaultTimeoutMs = 10000; // 10 seconds for WebSocket operations

    public RateLimitIntegrationTests(ProductionRateLimitServerFixture factory)
    {
        _factory = factory;
    }

    private static CancellationTokenSource CreateTimeoutCancellationTokenSource(int timeoutMs = DefaultTimeoutMs)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeoutMs);
        return cts;
    }

    [Fact]
    public async Task SenderTryCreateTooMuchSessionInRateLimit_ShouldFailWith429()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        Exception? exception = null;
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(SenderTryCreateTooMuchSessionInRateLimit_ShouldFailWith429);

        // Production rate limit: 5 requests per minute
        // Try 6 times - first 5 should succeed, 6th should fail with 429
        for (int tentative = 1; tentative <= 6; tentative++)
        {
            if (tentative < 6)
            {
                using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
                _ = await ReadCodeCreatedAsync(senderSocket, cts.Token);
                await CloseSocketSafeAsync(senderSocket, cts.Token);
            }
            else
            {
                exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token));
            }
        }

        Assert.NotNull(exception);
        Assert.Contains("status code: 429", exception.Message);
    }

    private static async Task<CodeCreatedResponse> ReadCodeCreatedAsync(WebSocket socket, CancellationToken ct)
    {
        string responseJson = await ReceiveTextMessageAsync(socket, ct);
        CodeCreatedResponse? codeCreatedResponse = JsonSerializer.Deserialize(responseJson, SignalingJsonContext.Default.CodeCreatedResponse);

        Assert.NotNull(codeCreatedResponse);
        Assert.Equal("CODE_CREATED", codeCreatedResponse.Type);
        Assert.False(string.IsNullOrWhiteSpace(codeCreatedResponse.Code));
        return codeCreatedResponse;
    }

    private static async Task<string> ReceiveTextMessageAsync(WebSocket socket, CancellationToken ct)
    {
        byte[] buffer = new byte[16 * 1024];
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, ct);

        Assert.Equal(WebSocketMessageType.Text, result.MessageType);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    private static async Task CloseSocketSafeAsync(WebSocket socket, CancellationToken ct)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                // Use a separate cleanup timeout (1 second) to prevent cleanup itself from hanging
                using CancellationTokenSource cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken, ct);
                cleanupCts.CancelAfter(1000);
                await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test-end", cleanupCts.Token);
            }
            catch
            {
                // Best effort for test cleanup.
            }
        }
    }
}
