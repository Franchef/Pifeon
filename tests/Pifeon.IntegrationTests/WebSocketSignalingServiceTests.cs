using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features; // Assicurati di includere questo namespace
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.IntegrationTests;

public sealed class WebSocketSignalingServiceTests : IAsyncLifetime
{
    private IHost _host = null!;
    private string _serverUrl = null!;

    public async Task InitializeAsync()
    {
        // Avvia Kestrel su una porta libera dinamica (0)
        _host = await new HostBuilder()
            .ConfigureWebHost(webBuilder =>
            {
                webBuilder
                    .UseKestrel(options =>
                    {
                        options.Listen(System.Net.IPAddress.Loopback, 0); // Porta dinamica
                    })
                    .ConfigureServices(_ => { })
                    .Configure(app =>
                    {
                        app.UseWebSockets();
                        app.Use(async (context, next) =>
                        {
                            if (context.WebSockets.IsWebSocketRequest)
                            {
                                using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
                                await HandleMockWebSocketAsync(webSocket);
                            }
                            else
                            {
                                await next();
                            }
                        });
                    });
            })
            .StartAsync();

        // 1. Recupera il server dall'IoC Container
        IServer server = _host.Services.GetRequiredService<IServer>();

        // 2. Estrai IServerAddressesFeature dalle Features del Server
        IServerAddressesFeature? serverAddresses = server.Features.Get<IServerAddressesFeature>();
        string address = serverAddresses!.Addresses.First();

        var builder = new UriBuilder(address)
        {
            Scheme = "ws",
            Path = "/ws/pairing"
        };
        _serverUrl = builder.Uri.ToString();
    }

    public async Task DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }

    [Fact]
    public void Constructor_WithValidUrl_ShouldInitializeService()
    {
        using var service = new WebSocketSignalingService(_serverUrl);
        Assert.NotNull(service);
    }

    [Fact]
    public async Task CreateSessionAsync_ValidResponse_ReturnsGeneratedCode()
    {
        using var service = new WebSocketSignalingService(_serverUrl);

        string code = await service.CreateSessionAsync();

        Assert.Equal("123456", code);
    }

    [Fact]
    public async Task CreateSessionAsync_InvalidServerResponse_ThrowsInvalidOperationException()
    {
        var builder = new UriBuilder(_serverUrl) { Query = "mode=invalid_response" };
        using var service = new WebSocketSignalingService(builder.Uri.ToString());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CreateSessionAsync());
    }

    [Fact]
    public async Task JoinSessionAsync_ConnectsAndSendsJoinRequest_ReturnsTrue()
    {
        using var service = new WebSocketSignalingService(_serverUrl);

        bool result = await service.JoinSessionAsync("123456");

        Assert.True(result);
    }

    [Fact]
    public async Task WaitForReceiverAsync_WhenSignaledByServer_CompletesSuccessfully()
    {
        using var service = new WebSocketSignalingService(_serverUrl);
        await service.CreateSessionAsync();

        // The mock server sends RECEIVER_JOINED right after session creation
        await service.WaitForReceiverAsync(CancellationToken.None);
    }

    [Fact]
    public async Task WaitForReceiverAsync_WhenCancelled_ShouldThrowTaskCanceledException()
    {
        using var service = new WebSocketSignalingService(_serverUrl);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            service.WaitForReceiverAsync(cts.Token));
    }

    [Fact]
    public async Task SendSignalDataAsync_WhenConnected_SendsMessageSuccessfully()
    {
        using var service = new WebSocketSignalingService(_serverUrl);
        await service.CreateSessionAsync();

        await service.SendSignalDataAsync("sample_sdp_payload");
    }

    [Fact]
    public async Task SendSignalDataAsync_WhenNotConnected_ShouldThrowInvalidOperationException()
    {
        using var service = new WebSocketSignalingService(_serverUrl);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.SendSignalDataAsync("sample_sdp_payload"));
    }

    [Fact]
    public async Task ListenLoopAsync_OnSignalDataMessage_FiresOnSignalDataReceivedEvent()
    {
        using var service = new WebSocketSignalingService(_serverUrl);
        string? receivedPayload = null;
        service.OnSignalDataReceived += payload => receivedPayload = payload;

        await service.CreateSessionAsync();

        // Wait briefly for ListenLoopAsync to receive the mock SIGNAL_DATA message
        await Task.Delay(100);

        Assert.Equal("test_payload_data", receivedPayload);
    }

    [Fact]
    public async Task ListenLoopAsync_HandlesMalformedJsonAndMissingProperties_Gracefully()
    {
        var builder = new UriBuilder(_serverUrl) { Query = "mode=edge_cases" };
        using var service = new WebSocketSignalingService(builder.Uri.ToString());
        string? receivedPayload = null;
        service.OnSignalDataReceived += payload => receivedPayload = payload;

        await service.CreateSessionAsync();
        await Task.Delay(150);

        // Fallback for null/missing payload prop should invoke with ""
        Assert.Equal("", receivedPayload);
    }

    [Fact]
    public async Task ListenLoopAsync_HandlesCloseFrameAndExceptions_Gracefully()
    {
        var builder = new UriBuilder(_serverUrl) { Query = "mode=close_and_exception" };
        using var service = new WebSocketSignalingService(builder.Uri.ToString());

        await service.CreateSessionAsync();
        await Task.Delay(100);
    }

    [Fact]
    public async Task DisconnectAsync_WhenConnected_ClosesSocketCleanly()
    {
        using var service = new WebSocketSignalingService(_serverUrl);
        await service.CreateSessionAsync();

        await service.DisconnectAsync();
    }

    [Fact]
    public async Task DisconnectAsync_WhenNotConnected_DoesNotThrow()
    {
        using var service = new WebSocketSignalingService(_serverUrl);

        await service.DisconnectAsync();
    }

    [Fact]
    public void Dispose_FreesWebSocketResources()
    {
        var service = new WebSocketSignalingService(_serverUrl);

        service.Dispose();
    }

    // --- Mock WebSocket Endpoint logic ---
    private static async Task HandleMockWebSocketAsync(WebSocket webSocket)
    {
        byte[] buffer = new byte[1024 * 4];
        WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        string requestJson = Encoding.UTF8.GetString(buffer, 0, result.Count);

        if (requestJson.Contains("CREATE"))
        {
            if (requestJson.Contains("invalid_response"))
            {
                byte[] errBytes = Encoding.UTF8.GetBytes("{\"type\":\"ERROR\",\"code\":\"\"}");
                await webSocket.SendAsync(new ArraySegment<byte>(errBytes), WebSocketMessageType.Text, true, CancellationToken.None);
                return;
            }

            // Valid CODE_CREATED response
            string responseJson = JsonSerializer.Serialize(
                new CodeCreatedResponse("CODE_CREATED", "123456"),
                SignalingJsonContext.Default.CodeCreatedResponse);
            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(responseJson)), WebSocketMessageType.Text, true, CancellationToken.None);

            if (requestJson.Contains("edge_cases"))
            {
                await Task.Delay(20);
                // JSON without "type" property
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"other\":\"val\"}")), WebSocketMessageType.Text, true, CancellationToken.None);

                await Task.Delay(20);
                // Unknown "type" value
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"UNKNOWN_TYPE\"}")), WebSocketMessageType.Text, true, CancellationToken.None);

                await Task.Delay(20);
                // SIGNAL_DATA without payload property (tests null fallback)
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"SIGNAL_DATA\"}")), WebSocketMessageType.Text, true, CancellationToken.None);
                return;
            }

            if (requestJson.Contains("close_and_exception"))
            {
                await Task.Delay(20);
                // Malformed JSON (Triggers catch block in ListenLoopAsync)
                await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{ malformed json ")), WebSocketMessageType.Text, true, CancellationToken.None);

                await Task.Delay(20);
                // Close frame (Triggers result.MessageType == WebSocketMessageType.Close)
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                return;
            }

            // Normal flow: notify receiver joined & send signal data
            await Task.Delay(20);
            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"RECEIVER_JOINED\"}")), WebSocketMessageType.Text, true, CancellationToken.None);

            await Task.Delay(20);
            await webSocket.SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"type\":\"SIGNAL_DATA\",\"payload\":\"test_payload_data\"}")), WebSocketMessageType.Text, true, CancellationToken.None);
        }

        // Passively maintain connection for incoming reads
        while (webSocket.State == WebSocketState.Open)
        {
            try
            {
                WebSocketReceiveResult recvResult = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (recvResult.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }
            }
            catch
            {
                break;
            }
        }
    }
}
