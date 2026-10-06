using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Moq;
using Moq.Protected;
using Pifeon.Core;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonServerTests : IDisposable
{
    private readonly Mock<HttpMessageHandler> _httpMessageHandlerMock;
    private readonly HttpClient _httpClient;
    private readonly PifeonServer _sut;

    public PifeonServerTests()
    {
        _httpMessageHandlerMock = new Mock<HttpMessageHandler>();
        _httpClient = new HttpClient(_httpMessageHandlerMock.Object);

        // Inizializziamo il System Under Test fornendo un HttpClient mockato
        _sut = new PifeonServer("ws://localhost:8080", null, _httpClient);
    }

    public void Dispose()
    {
        _sut.Dispose();
        _httpClient.Dispose();
    }

    #region 1. Constructor & Dependency Management Tests

    [Fact]
    public void Constructor_ShouldResolveServerUrl_WhenCustomUrlProvided()
    {
        // Arrange & Act
        using var server = new PifeonServer("ws://custom-server:5000", null, _httpClient);

        // Assert
        Assert.Equal("ws://custom-server:5000", server.ServerUrl);
    }

    [Fact]
    public void Constructor_ShouldInstantiateOwnHttpClient_WhenHttpClientIsNull()
    {
        // Arrange & Act (nessun HttpClient iniettato -> _ownsHttpClient = true)
        using var server = new PifeonServer("ws://localhost:8080");

        // Assert
        Assert.NotNull(server.ServerUrl);
    }

    #endregion

    #region 2. IsHealthyAsync Tests

    [Theory]
    [InlineData("ws://localhost:8080", "http://localhost:8080/health")]
    [InlineData("wss://secure-server.com:8080", "https://secure-server.com:8080/health")]
    [InlineData("http://localhost:8080", "http://localhost:8080/health")]
    [InlineData("https://localhost:8080", "https://localhost:8080/health")]
    public async Task IsHealthyAsync_ShouldReturnTrue_AndConvertUrlSchemeCorrectly_WhenStatusCodeIs200(
        string serverUrl,
        string expectedHealthUri)
    {
        // Arrange
        using var server = new PifeonServer(serverUrl, null, _httpClient);

        Uri? requestedUri = null;

        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => requestedUri = req.RequestUri)
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK
            });

        // Act
        bool result = await server.IsHealthyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.True(result);
        Assert.NotNull(requestedUri);
        Assert.Equal(expectedHealthUri, requestedUri.ToString());
    }

    [Fact]
    public async Task IsHealthyAsync_ShouldReturnFalse_WhenStatusCodeIsNotSuccess()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError
            });

        // Act
        bool result = await _sut.IsHealthyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public async Task IsHealthyAsync_ShouldReturnFalse_WhenHttpClientThrowsException()
    {
        // Arrange
        _httpMessageHandlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("Network failure"));

        // Act
        bool result = await _sut.IsHealthyAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.False(result);
    }

    #endregion

    #region 3. Network Connection Failures (CreateSession / JoinSession)

    [Fact]
    public async Task CreateSessionHandleAsync_ShouldRejectUnencryptedRemoteSignaling()
    {
        await using var server = new PifeonServer("ws://example.com", null, _httpClient);
        InvalidOperationException error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            server.CreateSessionHandleAsync(TestContext.Current.CancellationToken));
        Assert.Contains("requires WSS", error.Message);
    }

    [Fact]
    public async Task CreateSessionAsync_ShouldThrowException_WhenServerUnreachable()
    {
        // Act & Assert (In assenza di un endpoint WebSocket attivo, deve fallire la connessione)
        await Assert.ThrowsAnyAsync<Exception>(() => _sut.CreateSessionAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CreateSessionHandleAsync_ShouldThrowException_WhenServerUnreachable()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _sut.CreateSessionHandleAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JoinSessionAsync_ShouldThrowException_WhenServerUnreachable()
    {
        // Act & Assert
        await Assert.ThrowsAnyAsync<Exception>(() => _sut.JoinSessionAsync("123456", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task JoinSessionHandleAsync_ShouldThrowException_WhenServerUnreachable()
    {
        await Assert.ThrowsAnyAsync<Exception>(() => _sut.JoinSessionHandleAsync("123456", TestContext.Current.CancellationToken));
    }

    #endregion

    #region 4. Disposal & ObjectDisposedException Tests

    [Fact]
    public async Task Methods_ShouldThrowObjectDisposedException_WhenDisposed()
    {
        // Arrange
        var server = new PifeonServer("ws://localhost:8080", null, _httpClient);
        await server.DisposeAsync();

        // Act & Assert
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.CreateSessionHandleAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.JoinSessionHandleAsync("123456", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.CreateSessionAsync(TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.JoinSessionAsync("123456", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => server.IsHealthyAsync(TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task DisposeAsync_ShouldNotDisposeExternalHttpClient_WhenNotOwned()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var externalClient = new HttpClient(handlerMock.Object);
        var server = new PifeonServer("ws://localhost:8080", null, externalClient);

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

        // Act
        await server.DisposeAsync();

        // Assert: L'HttpClient esterno deve rimanere attivo ed operativo
        HttpResponseMessage response = await externalClient.GetAsync("http://localhost", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        externalClient.Dispose();
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        var server = new PifeonServer("ws://localhost:8080");

        // Act & Assert (La doppia chiamata a DisposeAsync non deve lanciare eccezioni)
        await server.DisposeAsync();
        await server.DisposeAsync();

        Assert.Empty(server.ServerUrl);
    }

    #endregion
}
