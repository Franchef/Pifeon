using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Models;

namespace Pifeon.Tests.CoreTests;

public class PairingManagerTests
{
    #region 1. Formato e Univocità dei Codici

    [Fact]
    public async Task CreateSessionAsync_ShouldReturnSixDigitNumericCode()
    {
        // Arrange
        var sut = new PairingManager<string>();

        // Act
        string code = await sut.CreateSessionAsync("Sender_Connection_1", TestContext.Current.CancellationToken);

        // Assert
        Assert.NotNull(code);
        Assert.Equal(6, code.Length);
        Assert.True(int.TryParse(code, out int numericCode), "Il codice deve essere numerico.");
        Assert.InRange(numericCode, 100000, 999999);
    }

    [Fact]
    public async Task CreateSessionAsync_MultipleCalls_ShouldGenerateUniqueCodes()
    {
        // Arrange
        var sut = new PairingManager<string>();
        var generatedCodes = new HashSet<string>();
        int iterations = 1000;

        // Act
        for (int i = 0; i < iterations; i++)
        {
            string code = await sut.CreateSessionAsync($"Sender_Connection_{i}", TestContext.Current.CancellationToken);
            bool added = generatedCodes.Add(code);

            Assert.True(added, $"Rilevata collisione del codice al tentativo {i}: {code}");
        }

        // Assert
        Assert.Equal(iterations, generatedCodes.Count);
    }

    #endregion

    #region 2. Gestione dello Stato (TryGetSession e RemoveSession)

    [Fact]
    public async Task TryGetSession_WithValidCode_ShouldReturnSessionAndTrue()
    {
        // Arrange
        var sut = new PairingManager<string>();
        string senderId = "Sender_Conn_ABC";
        string code = await sut.CreateSessionAsync(senderId, TestContext.Current.CancellationToken);

        // Act
        bool exists = sut.TryGetSession(code, out PairingSession<string>? session);

        // Assert
        Assert.True(exists);
        Assert.NotNull(session);
        Assert.Equal(code, session.Code);
        Assert.Equal(senderId, session.Sender);
    }

    [Fact]
    public void TryGetSession_WithNonExistentCode_ShouldReturnFalseAndNull()
    {
        // Arrange
        var sut = new PairingManager<string>();

        // Act
        bool exists = sut.TryGetSession("000000", out PairingSession<string>? session);

        // Assert
        Assert.False(exists);
        Assert.Null(session);
    }

    [Fact]
    public async Task RemoveSession_ShouldRemoveSessionFromMemoryAndDisposeCts()
    {
        // Arrange
        var sut = new PairingManager<string>();
        string code = await sut.CreateSessionAsync("Sender_Conn_1", TestContext.Current.CancellationToken);

        // Act
        sut.RemoveSession(code);
        bool exists = sut.TryGetSession(code, out _);

        // Assert
        Assert.False(exists, "La sessione avrebbe dovuto essere rimossa dal dizionario.");
    }

    [Fact]
    public void RemoveSession_WithInvalidCode_ShouldNotThrowException()
    {
        // Arrange
        var sut = new PairingManager<string>();

        // Act & Assert
        Exception exception = Record.Exception(() => sut.RemoveSession("NON_EXISTENT_CODE"));
        Assert.Null(exception);
    }

    #endregion

    #region 3. Flusso di Join e Sincronizzazione P2P (TryJoinSessionAsync)

    [Fact]
    public async Task TryJoinSessionAsync_WithValidCode_ShouldSetReceiverAndReturnTrue()
    {
        // Arrange
        var sut = new PairingManager<string>();
        string senderConnection = "Sender_Conn_1";
        string receiverConnection = "Receiver_Conn_2";

        string code = await sut.CreateSessionAsync(senderConnection, TestContext.Current.CancellationToken);

        // Act
        bool joinSuccess = await sut.TryJoinSessionAsync(code, receiverConnection, TestContext.Current.CancellationToken);

        // Assert
        Assert.True(joinSuccess);

        sut.TryGetSession(code, out PairingSession<string>? session);
        Assert.NotNull(session);

        string actualReceiver = await session.ReceiverConnected.Task;
        Assert.Equal(receiverConnection, actualReceiver);
    }

    [Fact]
    public async Task TryJoinSessionAsync_WithInvalidCode_ShouldReturnFalse()
    {
        // Arrange
        var sut = new PairingManager<string>();

        // Act
        bool joinSuccess = await sut.TryJoinSessionAsync("999999", "Receiver_Conn_2", TestContext.Current.CancellationToken);

        // Assert
        Assert.False(joinSuccess);
    }

    [Fact]
    public async Task TryJoinSessionAsync_DuplicateJoin_ShouldReturnFalse()
    {
        // Arrange
        var sut = new PairingManager<string>();
        string code = await sut.CreateSessionAsync("Sender_Conn_1", TestContext.Current.CancellationToken);

        // Act: Il primo receiver si connette con successo
        bool firstJoin = await sut.TryJoinSessionAsync(code, "Receiver_Conn_1", TestContext.Current.CancellationToken);

        // Act: Un secondo receiver tenta la join sulla stessa sessione
        bool secondJoin = await sut.TryJoinSessionAsync(code, "Receiver_Conn_2", TestContext.Current.CancellationToken);

        // Assert
        Assert.True(firstJoin);
        Assert.False(secondJoin, "Un secondo tentativo di join su una sessione già accoppiata deve fallire.");
    }

    #endregion

    #region 4. Timeout e Scadenza Automatica

    [Fact]
    public async Task Session_OnTimeout_ShouldAutomaticallyRemoveItself()
    {
        // Arrange: Inizializziamo PairingManager con un timeout ridotto a 100 ms
        TimeSpan shortTimeout = TimeSpan.FromMilliseconds(100);
        var sut = new PairingManager<string>(shortTimeout);

        string code = await sut.CreateSessionAsync("Sender_Conn_1", TestContext.Current.CancellationToken);

        // Verifichiamo che la sessione sia inizialmente presente
        Assert.True(sut.TryGetSession(code, out _));

        // Act: Attendiamo la scadenza del timer di timeout
        await Task.Delay(250, TestContext.Current.CancellationToken);

        // Assert
        bool existsAfterTimeout = sut.TryGetSession(code, out _);
        Assert.False(existsAfterTimeout, "La sessione doveva essere rimossa automaticamente alla scadenza del timeout.");
    }

    [Fact]
    public async Task Session_ManualDispose_ShouldCancelTimeoutToken()
    {
        // Arrange
        var sut = new PairingManager<string>();
        string code = await sut.CreateSessionAsync("Sender_Conn_1", TestContext.Current.CancellationToken);

        sut.TryGetSession(code, out PairingSession<string>? session);
        Assert.NotNull(session);

        // Act
        session.Dispose();

        // Assert
        Assert.True(session.TimeoutCts.IsCancellationRequested);
    }

    #endregion
}
