using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Models;
using Pifeon.Server;

namespace Pifeon.Tests.ServerTests;

public class PairingManagerTests
{

    [Fact]
    public async Task TestPairingManagerCreateSessionAsync()
    {
        // Arrange
        PairingManager<bool> sut = new();
        // Act
        string output = await sut.CreateSessionAsync(true, TestContext.Current.CancellationToken);
        // Assert
        Assert.False(string.IsNullOrEmpty(output), "CreateSession should return a non-empty code.");
    }

    [Fact]
    public async Task CreateSession_ShouldReturnSixDigitCodeAsync()
    {
        // Arrange
        PairingManager<bool> sut = new();

        // Act
        string code = await sut.CreateSessionAsync(true, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(int.TryParse(code, out int numericCode), "Il codice deve essere numerico.");
        Assert.InRange(numericCode, 100000, 999999);
    }

    [Fact]
    public async Task CreateSession_MultipleCalls_ShouldGenerateUniqueCodesAsync()
    {
        // Arrange
        PairingManager<bool> sut = new();
        HashSet<string> codes = [];
        int iterations = 1000;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            string code = await sut.CreateSessionAsync(true, TestContext.Current.CancellationToken);
            codes.Add(code);
        }

        // Assert: Nessuna collisione trovata
        Assert.Equal(iterations, codes.Count);
    }

    [Fact]
    public async Task TryGetSession_WithValidCode_ShouldReturnSessionAsync()
    {
        // Arrange
        PairingManager<bool> sut = new();
        string code = await sut.CreateSessionAsync(true, TestContext.Current.CancellationToken);

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
    public async Task RemoveSession_ShouldDeleteSessionFromMemoryAsync()
    {
        // Arrange
        PairingManager<bool> sut = new();
        string code = await sut.CreateSessionAsync(true, TestContext.Current.CancellationToken);

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

        string code = await sut.CreateSessionAsync(senderConnection, TestContext.Current.CancellationToken);
        sut.TryGetSession(code, out PairingSession<string>? session);

        // Act: Il Receiver si connette e imposta il suo risultato
        session!.ReceiverConnected.TrySetResult(receiverConnection);

        // Assert: Il Sender sblocca l'attesa e riceve la connessione del Receiver
        string connectedReceiver = await session.ReceiverConnected.Task;
        Assert.Equal(receiverConnection, connectedReceiver);
    }

    [Fact]
    public async Task Session_OnCancellation_ShouldRemoveSelfFromManagerAsync()
    {
        // Arrange
        PairingManager<bool> sut = new();
        string code = await sut.CreateSessionAsync(true, TestContext.Current.CancellationToken);

        sut.TryGetSession(code, out PairingSession<bool>? session);

        // Act: Simula la scadenza del CancellationToken (timeout 5 min)
        await session!.TimeoutCts.CancelAsync();

        // Assert
        bool exists = sut.TryGetSession(code, out _);
        Assert.False(exists, "La sessione cancellata/scaduta deve essere rimossa automaticamente.");
    }
}
