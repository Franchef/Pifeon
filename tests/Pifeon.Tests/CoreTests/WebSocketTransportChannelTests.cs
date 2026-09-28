using System;
using System.Collections.Generic;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using Pifeon.Core.Networking;

namespace Pifeon.Tests.CoreTests;

public sealed class WebSocketTransportChannelTests : IAsyncLifetime
{
    private HttpListener? _httpListener;
    private string _serverUrl = string.Empty;
    private readonly CancellationTokenSource _cts = new();

    public async ValueTask InitializeAsync()
    {
        // Trova una porta disponibile per l'HttpListener locale
        int port = GetAvailablePort();
        _serverUrl = $"ws://localhost:{port}/ws/";

        _httpListener = new HttpListener();
        _httpListener.Prefixes.Add($"http://localhost:{port}/ws/");
        _httpListener.Start();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _httpListener?.Stop();
        _httpListener?.Close();
        _cts.Dispose();
        await Task.CompletedTask;
    }

    #region 1. Unit Test - Costruttore e Validazione Input

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenWebSocketIsNull()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => new WebSocketTransportChannel(null!));
    }

    [Fact]
    public async Task IsConnected_ShouldReturnFalse_WhenWebSocketIsNotOpenAsync()
    {
        // Arrange
        using var webSocket = new ClientWebSocket();
        await using var channel = new WebSocketTransportChannel(webSocket);

        // Assert
        Assert.False(channel.IsConnected);
    }

    [Fact]
    public async Task SendAsync_ShouldThrowInvalidOperationException_WhenNotConnected()
    {
        // Arrange
        using var webSocket = new ClientWebSocket();
        await using var channel = new WebSocketTransportChannel(webSocket);
        byte[] data = "Hello"u8.ToArray();

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.SendAsync(data, _cts.Token).AsTask());
        Assert.Contains("Il canale WebSocket non è connesso", ex.Message);
    }

    [Fact]
    public async Task ReceiveAsync_ShouldThrowInvalidOperationException_WhenNotConnected()
    {
        // Arrange
        using var webSocket = new ClientWebSocket();
        await using var channel = new WebSocketTransportChannel(webSocket);
        byte[] buffer = new byte[1024];

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => channel.ReceiveAsync(buffer, _cts.Token).AsTask());
        Assert.Contains("Il canale WebSocket non è connesso", ex.Message);
    }

    [Fact]
    public async Task ConnectAsync_ShouldThrowException_WhenServerIsUnreachable()
    {
        // Arrange: URL su una porta non in ascolto
        string invalidUrl = "ws://localhost:59999/invalid";

        // Act & Assert
        await Assert.ThrowsAsync<WebSocketException>(() => WebSocketTransportChannel.ConnectAsync(invalidUrl, _cts.Token));
    }

    #endregion

    #region 2. Integration Test - Flusso di Rete Completo (Send, Receive, Close)

    [Fact]
    public async Task CompleteCommunicationFlow_ShouldWorkAsExpected()
    {
        // Arrange: Avviamo il server in background per gestire la connessione
        var serverTask = Task.Run(async () =>
        {
            HttpListenerContext context = await _httpListener!.GetContextAsync();
            if (context.Request.IsWebSocketRequest)
            {
                HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
                WebSocket serverSocket = wsContext.WebSocket;

                // Riceve il messaggio inviato dal client
                byte[] serverBuffer = new byte[1024];
                WebSocketReceiveResult receiveResult = await serverSocket.ReceiveAsync(serverBuffer, CancellationToken.None);

                // Risponde eco al client
                await serverSocket.SendAsync(
                    new ArraySegment<byte>(serverBuffer, 0, receiveResult.Count),
                    WebSocketMessageType.Binary,
                    endOfMessage: true,
                    CancellationToken.None);

                // Attende il frame di chiusura
                byte[] closeBuffer = new byte[100];
                await serverSocket.ReceiveAsync(closeBuffer, CancellationToken.None);
                await serverSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closed by server", CancellationToken.None);
            }
        }, _cts.Token);

        // Act 1: Connessione
        WebSocketTransportChannel channel = await WebSocketTransportChannel.ConnectAsync(_serverUrl, _cts.Token);
        Assert.True(channel.IsConnected);

        // Act 2: Invia Dati
        byte[] payload = "Hello Pifeon"u8.ToArray();
        await channel.SendAsync(payload, _cts.Token);

        // Act 3: Riceve Dati
        byte[] receiveBuffer = new byte[1024];
        int bytesRead = await channel.ReceiveAsync(receiveBuffer, _cts.Token);

        Assert.Equal(payload.Length, bytesRead);
        Assert.Equal(payload, receiveBuffer[..bytesRead]);

        // Act 4: Chiusura
        await channel.CloseAsync(_cts.Token);
        await serverTask;

        // Act 5: Teardown
        await channel.DisposeAsync();
    }

    [Fact]
    public async Task ReceiveAsync_ShouldReturnZeroAndClose_WhenServerSendsCloseMessageType()
    {
        // Arrange: Server che invia subito un frame di chiusura
        var serverTask = Task.Run(async () =>
        {
            HttpListenerContext context = await _httpListener!.GetContextAsync();
            HttpListenerWebSocketContext wsContext = await context.AcceptWebSocketAsync(subProtocol: null);
            WebSocket serverSocket = wsContext.WebSocket;

            // Invia il frame di chiusura
            await serverSocket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "Server closing", CancellationToken.None);
        }, _cts.Token);

        WebSocketTransportChannel channel = await WebSocketTransportChannel.ConnectAsync(_serverUrl, _cts.Token);
        byte[] buffer = new byte[1024];

        // Act
        int bytesRead = await channel.ReceiveAsync(buffer, _cts.Token);

        // Assert: Se il messaggio ricevuto è Close, il metodo restituisce 0 e chiama CloseAsync
        Assert.Equal(0, bytesRead);

        await serverTask;
        await channel.DisposeAsync();
    }

    #endregion

    #region 3. Unit Test - Disposal & Ownership Behavior

    [Fact]
    public async Task DisposeAsync_ShouldNotDisposeWebSocket_WhenOwnsWebSocketIsFalse()
    {
        // Arrange
        using var webSocket = new ClientWebSocket();
        var channel = new WebSocketTransportChannel(webSocket, ownsWebSocket: false);

        // Act
        await channel.DisposeAsync();

        // Assert: Il socket sottostante NON deve essere stato distrutto (Stato ancora accessible, es. None)
        Assert.Equal(WebSocketState.None, webSocket.State);
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        using var webSocket = new ClientWebSocket();
        var channel = new WebSocketTransportChannel(webSocket, ownsWebSocket: true);

        // Act & Assert (chiamate multiple non devono sollevare eccezioni)
        await channel.DisposeAsync();
        await channel.DisposeAsync();

        Assert.Null(webSocket.CloseStatus); // Il socket dovrebbe essere chiuso correttamente
    }

    #endregion

    #region Helpers

    private static int GetAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion
}
