using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
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
    public async Task BothClientsJoinSession_AndExchangeLocalIp_ShouldRelaySignalData()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(BothClientsJoinSession_AndExchangeLocalIp_ShouldRelaySignalData);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);

        _ = await ReceiveTextMessageAsync(senderSocket, cts.Token); // RECEIVER_JOINED

        string ipPayload = JsonSerializer.Serialize(new SignalDataMessage("SIGNAL_DATA", "192.168.1.42:53321"), SignalingJsonContext.Default.SignalDataMessage);
        await receiverSocket.SendAsync(Encoding.UTF8.GetBytes(ipPayload), WebSocketMessageType.Text, true, cts.Token);

        string relayed = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        Assert.Contains("SIGNAL_DATA", relayed);
        Assert.Contains("192.168.1.42", relayed);

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);
    }

    [Fact]
    public async Task BothClientsJoinSession_AndExchangeLocalIp_ThenDisconnect_ShouldCloseGracefully()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(BothClientsJoinSession_AndExchangeLocalIp_ThenDisconnect_ShouldCloseGracefully);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);
        _ = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        string ipPayload = JsonSerializer.Serialize(new SignalDataMessage("SIGNAL_DATA", "10.0.0.8:6000"), SignalingJsonContext.Default.SignalDataMessage);
        await senderSocket.SendAsync(Encoding.UTF8.GetBytes(ipPayload), WebSocketMessageType.Text, true, cts.Token);
        _ = await ReceiveTextMessageAsync(receiverSocket, cts.Token);

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);

        Assert.True(senderSocket.State is WebSocketState.CloseSent or WebSocketState.Closed);
        Assert.True(receiverSocket.State is WebSocketState.CloseSent or WebSocketState.Closed);
    }

    [Fact]
    public async Task BothClientsJoinSession_AndExchangeBinaryPayload_ShouldRelayAsFileChunkSuccessCase()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(BothClientsJoinSession_AndExchangeBinaryPayload_ShouldRelayAsFileChunkSuccessCase);

        using WebSocket senderSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        CodeCreatedResponse codeCreatedResponse = await ReadCodeCreatedAsync(senderSocket, cts.Token);

        using WebSocket receiverSocket = await wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreatedResponse.Code}"), cts.Token);
        _ = await ReceiveTextMessageAsync(senderSocket, cts.Token);

        byte[] payload = Enumerable.Range(1, 128).Select(i => (byte)i).ToArray();
        await senderSocket.SendAsync(payload, WebSocketMessageType.Binary, true, cts.Token);

        byte[] relayed = await ReceiveBinaryMessageAsync(receiverSocket, cts.Token);

        Assert.Equal(payload, relayed);

        await CloseSocketSafeAsync(receiverSocket, cts.Token);
        await CloseSocketSafeAsync(senderSocket, cts.Token);
    }

    [Fact]
    public async Task BothClientsJoinSession_AndExchangeFailureNotification_ShouldRelayAsFileChunkFailureCase()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(BothClientsJoinSession_AndExchangeFailureNotification_ShouldRelayAsFileChunkFailureCase);

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

    private static async Task<byte[]> ReceiveBinaryMessageAsync(WebSocket socket, CancellationToken ct)
    {
        byte[] buffer = new byte[16 * 1024];
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, ct);

        Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
        return buffer.AsSpan(0, result.Count).ToArray();
    }

    private static async Task CloseSocketSafeAsync(WebSocket socket, CancellationToken ct)
    {
        if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                // Use a separate cleanup timeout (1 second) to prevent cleanup itself from hanging
                using CancellationTokenSource cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
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

        await Task.Delay(TimeSpan.FromMilliseconds(700), cts.Token);

        InvalidOperationException exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            wsClient.ConnectAsync(new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{codeCreated.Code}"), cts.Token));

        Assert.Contains("status code: 404", exception.Message);

        // Socket may have been closed by server timeout (350ms), so gracefully handle disposal
        if (senderSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await senderSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "test-end", cts.Token);
            }
            catch
            {
                // Socket may be disposed by server-side cleanup; this is expected
            }
        }
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
                using CancellationTokenSource cleanupCts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
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
