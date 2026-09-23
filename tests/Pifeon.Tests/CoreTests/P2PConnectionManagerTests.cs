using System.Net;
using Pifeon.Core.Networking;

namespace Pifeon.Tests.CoreTests;

public class P2PConnectionManagerTests
{
    [Fact]
    public async Task LocalP2PConnection_ShouldConnectAndTransferDataIntegrally()
    {
        // Arrange
        int port = 49152 + Random.Shared.Next(0, 1000); // Porta casuale per evitare conflitti
        using var listener = new P2PConnectionManager();
        using var client = new P2PConnectionManager();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Act 1: Avviamo l'ascolto sul listener
        var listenTask = listener.ListenForPeerAsync(port, cts.Token);

        // Attesa breve per assicurare che il socket sia pronto
        await Task.Delay(100, cts.Token);

        // Act 2: Il client si connette
        var connectSuccess = await client.ConnectToPeerAsync(
            new IPEndPoint(IPAddress.Loopback, port),
            cts.Token);

        var listenSuccess = await listenTask;

        Assert.True(connectSuccess);
        Assert.True(listenSuccess);
        Assert.True(listener.IsConnected);
        Assert.True(client.IsConnected);

        // Act 3: Invio e ricezione dati
        byte[] expectedPayload = "P2P Stream Test Data Payload"u8.ToArray();
        await client.SendBytesAsync(expectedPayload, cts.Token);
        byte[] receivedPayload = await listener.ReceiveBytesAsync(cts.Token);

        // Assert
        Assert.Equal(expectedPayload, receivedPayload);
    }

    [Fact]
    public async Task SendBytesAsync_WhenDisconnected_ShouldThrowInvalidOperationException()
    {
        // Arrange
        using var manager = new P2PConnectionManager();

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            manager.SendBytesAsync(new byte[] { 1, 2, 3 }));
    }
}
