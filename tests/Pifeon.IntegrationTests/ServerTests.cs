using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using Microsoft.VisualStudio.TestPlatform.TestHost;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.IntegrationTests;

public class SignalingIntegrationTests : IClassFixture<ServerFixture>
{
    private readonly ServerFixture _factory;

    public SignalingIntegrationTests(ServerFixture factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task FullPairingFlow_SenderAndReceiver_ShouldConnectSuccessfully()
    {
        // Arrange
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        var serverUri = new Uri(_factory.Server.BaseAddress, "/ws/pairing");

        using WebSocket senderSocket = await wsClient.ConnectAsync(serverUri, CancellationToken.None);
        using WebSocket receiverSocket = await wsClient.ConnectAsync(serverUri, CancellationToken.None);

        // Act 1: Il SENDER richiede la creazione della sessione
        byte[] createPayload = JsonSerializer.SerializeToUtf8Bytes(
            new CreateSessionRequest("CREATE"),
            SignalingJsonContext.Default.CreateSessionRequest);

        await senderSocket.SendAsync(createPayload, WebSocketMessageType.Text, true, CancellationToken.None);

        // Il SENDER riceve il codice a 6 cifre
        byte[] buffer = new byte[1024];
        WebSocketReceiveResult result = await senderSocket.ReceiveAsync(buffer, CancellationToken.None);
        string responseJson = Encoding.UTF8.GetString(buffer, 0, result.Count);
        CodeCreatedResponse? codeCreatedResponse = JsonSerializer.Deserialize(responseJson, SignalingJsonContext.Default.CodeCreatedResponse);

        Assert.NotNull(codeCreatedResponse);
        Assert.Equal("CODE_CREATED", codeCreatedResponse.Type);
        Assert.False(string.IsNullOrEmpty(codeCreatedResponse.Code));

        // Act 2: Il RECEIVER fa la JOIN usando il codice generato
        byte[] joinPayload = JsonSerializer.SerializeToUtf8Bytes(
            new JoinSessionRequest(codeCreatedResponse.Code, "JOIN"),
            SignalingJsonContext.Default.JoinSessionRequest);

        await receiverSocket.SendAsync(joinPayload, WebSocketMessageType.Text, true, CancellationToken.None);

        // Assert: Il SENDER riceve la notifica che il RECEIVER si è connesso
        byte[] senderBuffer = new byte[1024];
        WebSocketReceiveResult senderResult = await senderSocket.ReceiveAsync(senderBuffer, CancellationToken.None);
        string senderNotificationJson = Encoding.UTF8.GetString(senderBuffer, 0, senderResult.Count);

        Assert.Contains("RECEIVER_JOINED", senderNotificationJson);
    }
}
