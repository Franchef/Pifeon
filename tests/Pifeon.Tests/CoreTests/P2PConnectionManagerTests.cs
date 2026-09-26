using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Pifeon.Core.Networking; // Adatta con il namespace corretto del tuo progetto
using Xunit;

namespace Pifeon.Tests.Unit.Networking;

public class P2PConnectionManagerTests
{
    #region 1. Test di Stato e Connessione

    [Fact]
    public void IsConnected_ShouldReturnFalse_WhenNotConnected()
    {
        // Arrange
        using var manager = new P2PConnectionManager();

        // Assert
        Assert.False(manager.IsConnected);
    }

    [Fact]
    public async Task SendBytesAsync_ShouldThrowInvalidOperationException_WhenNotConnected()
    {
        // Arrange
        await using var manager = new P2PConnectionManager();
        byte[] payload = "Hello P2P"u8.ToArray();

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.SendBytesAsync(payload));
        Assert.Contains("Socket P2P non connesso", ex.Message);
    }

    [Fact]
    public async Task ReceiveBytesAsync_ShouldThrowInvalidOperationException_WhenNotConnected()
    {
        // Arrange
        await using var manager = new P2PConnectionManager();

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => manager.ReceiveBytesAsync());
        Assert.Contains("Socket P2P non connesso", ex.Message);
    }

    [Fact]
    public async Task ConnectToPeerAsync_ShouldReturnFalse_WhenEndpointIsUnreachable()
    {
        // Arrange: Utilizziamo un IPEndPoint con una porta non aperta
        int port = GetAvailablePort();
        var unreachableEndPoint = new IPEndPoint(IPAddress.Loopback, port);
        await using var manager = new P2PConnectionManager();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        bool result = await manager.ConnectToPeerAsync(unreachableEndPoint, cts.Token);

        // Assert
        Assert.False(result);
        Assert.False(manager.IsConnected);
    }

    [Fact]
    public async Task ListenForPeerAsync_ShouldReturnFalse_WhenCancelled()
    {
        // Arrange
        int port = GetAvailablePort();
        await using var manager = new P2PConnectionManager();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        bool result = await manager.ListenForPeerAsync(port, cts.Token);

        // Assert
        Assert.False(result);
        Assert.False(manager.IsConnected);
    }

    #endregion

    #region 2. Test Flusso di Rete Completo (Listen, Connect, Send, Receive)

    [Fact]
    public async Task CompleteP2PFlow_ShouldExchangeBytesSuccessfully()
    {
        // Arrange
        int port = GetAvailablePort();
        var endPoint = new IPEndPoint(IPAddress.Loopback, port);

        await using var senderManager = new P2PConnectionManager();
        await using var receiverManager = new P2PConnectionManager();

        byte[] payloadToSend = "Payload di prova Pifeon P2P"u8.ToArray();

        // Act 1: Il mittente si mette in ascolto in background
        Task<bool> listenTask = Task.Run(() => senderManager.ListenForPeerAsync(port));

        // Diamo tempo al socket in ascolto di avviarsi
        await Task.Delay(50);

        // Act 2: Il ricevitore si connette
        bool connected = await receiverManager.ConnectToPeerAsync(endPoint);
        bool listenResult = await listenTask;

        // Assert connessione stabilita
        Assert.True(connected);
        Assert.True(listenResult);
        Assert.True(senderManager.IsConnected);
        Assert.True(receiverManager.IsConnected);

        // Act 3: Invio dal mittente al ricevitore
        Task sendTask = senderManager.SendBytesAsync(payloadToSend);
        byte[] receivedPayload = await receiverManager.ReceiveBytesAsync();
        await sendTask;

        // Assert integrità dati
        Assert.Equal(payloadToSend, receivedPayload);
    }

    [Fact]
    public async Task ReceiveBytesAsync_ShouldThrowSocketException_WhenConnectionIsResetByPeer()
    {
        // Arrange
        int port = GetAvailablePort();
        var endPoint = new IPEndPoint(IPAddress.Loopback, port);

        await using var listenerManager = new P2PConnectionManager();
        var clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

        Task<bool> listenTask = Task.Run(() => listenerManager.ListenForPeerAsync(port));
        await Task.Delay(50);

        await clientSocket.ConnectAsync(endPoint);
        await listenTask;

        // Chiudiamo bruscamente il socket client per simulare il reset della connessione
        clientSocket.Close();

        // Act & Assert: ReadExactAsync rileva 0 byte letti e lancia SocketException
        await Assert.ThrowsAsync<SocketException>(() => listenerManager.ReceiveBytesAsync());
    }

    #endregion

    #region 3. Test Ciclo di Vita (Dispose / DisposeAsync)

    [Fact]
    public async Task DisposeAsync_ShouldCloseConnectionAndBeIdempotent()
    {
        // Arrange
        int port = GetAvailablePort();
        var endPoint = new IPEndPoint(IPAddress.Loopback, port);

        var senderManager = new P2PConnectionManager();
        await using var receiverManager = new P2PConnectionManager();

        Task<bool> listenTask = Task.Run(() => senderManager.ListenForPeerAsync(port));
        await Task.Delay(50);

        await receiverManager.ConnectToPeerAsync(endPoint);
        await listenTask;

        Assert.True(senderManager.IsConnected);

        // Act & Assert: Eseguiamo il DisposeAsync
        await senderManager.DisposeAsync();
        Assert.False(senderManager.IsConnected);

        // Chiamate successive non devono sollevare eccezioni (Idempotenza)
        await senderManager.DisposeAsync();
    }

    [Fact]
    public async Task Dispose_ShouldWorkFallbackSynchronously()
    {
        // Arrange
        int port = GetAvailablePort();
        var endPoint = new IPEndPoint(IPAddress.Loopback, port);

        var senderManager = new P2PConnectionManager();
        await using var receiverManager = new P2PConnectionManager();

        Task<bool> listenTask = Task.Run(() => senderManager.ListenForPeerAsync(port));
        await Task.Delay(50);

        await receiverManager.ConnectToPeerAsync(endPoint);
        await listenTask;

        // Act: Dispose sincrono
        senderManager.Dispose();

        // Assert
        Assert.False(senderManager.IsConnected);
    }

    #endregion

    #region Helpers

    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    #endregion
}
