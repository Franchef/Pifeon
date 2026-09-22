using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Server;

namespace Pifeon.Tests.ServerTests;

public class PairingManagerTests
{

    [Fact]
    public void TestPairingManagerCreateSession()
    {
        // Arrange
        PairingManager<bool> sut = new();
        // Act
        string output = sut.CreateSession(true);
        // Assert
        Assert.False(string.IsNullOrEmpty(output), "CreateSession should return a non-empty code.");
    }

    [Fact]
    public void CreateSession_ShouldReturnSixDigitCode()
    {
        // Arrange
        PairingManager<bool> sut = new();

        // Act
        string code = sut.CreateSession(true);

        // Assert
        Assert.True(int.TryParse(code, out int numericCode), "Il codice deve essere numerico.");
        Assert.InRange(numericCode, 100000, 999999);
    }

    [Fact]
    public void CreateSession_MultipleCalls_ShouldGenerateUniqueCodes()
    {
        // Arrange
        PairingManager<bool> sut = new();
        HashSet<string> codes = [];
        int iterations = 1000;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            string code = sut.CreateSession(true);
            codes.Add(code);
        }

        // Assert: Nessuna collisione trovata
        Assert.Equal(iterations, codes.Count);
    }

    [Fact]
    public void TryGetSession_WithValidCode_ShouldReturnSession()
    {
        // Arrange
        PairingManager<bool> sut = new();
        string code = sut.CreateSession(true);

        // Act
        bool success = sut.TryGetSession(code, out PairingSession<bool>? session);

        // Assert
        Assert.True(success);
        Assert.NotNull(session);
        Assert.True(session.Sender);
    }

    [Fact]
    public void TryGetSession_WithInvalidCode_ShouldReturnFalse()
    {
        // Arrange
        PairingManager<bool> sut = new();

        // Act
        bool success = sut.TryGetSession("000000", out PairingSession<bool>? session);

        // Assert
        Assert.False(success);
        Assert.Null(session);
    }

    [Fact]
    public void RemoveSession_ShouldDeleteSessionFromMemory()
    {
        // Arrange
        PairingManager<bool> sut = new();
        string code = sut.CreateSession(true);

        // Act
        sut.RemoveSession(code);
        bool success = sut.TryGetSession(code, out _);

        // Assert
        Assert.False(success, "La sessione avrebbe dovuto essere rimossa dalla memoria.");
    }

    [Fact]
    public async Task JoinSession_ShouldCompleteReceiverTask()
    {
        // Arrange
        PairingManager<string> sut = new();
        string senderConnection = "Sender_Socket_ID";
        string receiverConnection = "Receiver_Socket_ID";

        string code = sut.CreateSession(senderConnection);
        sut.TryGetSession(code, out PairingSession<string>? session);

        // Act: Il Receiver si connette e imposta il suo risultato
        session!.ReceiverConnected.TrySetResult(receiverConnection);

        // Assert: Il Sender sblocca l'attesa e riceve la connessione del Receiver
        string connectedReceiver = await session.ReceiverConnected.Task;
        Assert.Equal(receiverConnection, connectedReceiver);
    }

    [Fact]
    public void Session_OnCancellation_ShouldRemoveSelfFromManager()
    {
        // Arrange
        PairingManager<bool> sut = new();
        string code = sut.CreateSession(true);

        sut.TryGetSession(code, out PairingSession<bool>? session);

        // Act: Simula la scadenza del CancellationToken (timeout 5 min)
        session!.TimeoutCts.Cancel();

        // Assert
        bool exists = sut.TryGetSession(code, out _);
        Assert.False(exists, "La sessione cancellata/scaduta deve essere rimossa automaticamente.");
    }
}
