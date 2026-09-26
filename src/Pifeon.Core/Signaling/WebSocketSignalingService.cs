using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Signaling;

public sealed class WebSocketSignalingService : ISignalingService, IDisposable
{
    private readonly Uri _serverUri;
    private ClientWebSocket? _webSocket;
    private readonly TaskCompletionSource<bool> _receiverJoinedTcs = new();

    public event Action<string>? OnSignalDataReceived;

    public WebSocketSignalingService(string serverUrl)
    {
        _serverUri = new Uri(serverUrl);
    }

    public async Task<string> CreateSessionAsync(CancellationToken cancellationToken = default)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(_serverUri, cancellationToken);

        // Invia la richiesta "CREATE"
        var request = new CreateSessionRequest();
        string json = JsonSerializer.Serialize(request, SignalingJsonContext.Default.CreateSessionRequest);
        await SendTextAsync(json, cancellationToken);

        // Riceve la risposta con il codice a 6 cifre
        string responseJson = await ReceiveTextAsync(cancellationToken);
        CodeCreatedResponse? response = JsonSerializer.Deserialize(responseJson, SignalingJsonContext.Default.CodeCreatedResponse);

        if (response?.Type == "CODE_CREATED" && !string.IsNullOrEmpty(response.Code))
        {
            // Avvia il loop di ascolto in background per notifiche e segnali
            _ = ListenLoopAsync(cancellationToken);
            return response.Code;
        }

        throw new InvalidOperationException("Impossibile generare il codice di pairing dal server.");
    }

    public async Task WaitForReceiverAsync(CancellationToken cancellationToken = default)
    {
        // Attende che il loop di ascolto riceva il messaggio RECEIVER_JOINED
        using CancellationTokenRegistration registration = cancellationToken.Register(() => _receiverJoinedTcs.TrySetCanceled(cancellationToken));
        await _receiverJoinedTcs.Task;
    }

    public async Task<bool> JoinSessionAsync(string code, CancellationToken cancellationToken = default)
    {
        _webSocket = new ClientWebSocket();
        await _webSocket.ConnectAsync(_serverUri, cancellationToken);

        // Invia la richiesta "JOIN" con il codice a 6 cifre
        var request = new JoinSessionRequest(code);
        string json = JsonSerializer.Serialize(request, SignalingJsonContext.Default.JoinSessionRequest);
        await SendTextAsync(json, cancellationToken);

        // Avvia il loop di ascolto
        _ = ListenLoopAsync(cancellationToken);
        return true;
    }

    public async Task SendSignalDataAsync(string payload, CancellationToken cancellationToken = default)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket non connesso.");
        }

        var message = new SignalDataMessage("SIGNAL_DATA", payload);
        string json = JsonSerializer.Serialize(message, SignalingJsonContext.Default.SignalDataMessage);
        await SendTextAsync(json, cancellationToken);
    }

    public async Task DisconnectAsync(CancellationToken cancellationToken = default)
    {
        if (_webSocket != null && _webSocket.State == WebSocketState.Open)
        {
            await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", cancellationToken);
        }
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 8];

        try
        {
            while (_webSocket?.State == WebSocketState.Open && !ct.IsCancellationRequested)
            {
                WebSocketReceiveResult result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                string json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                using var doc = JsonDocument.Parse(json);

                if (doc.RootElement.TryGetProperty("type", out JsonElement typeProp))
                {
                    string type = typeProp.GetString() ?? "";

                    switch (type)
                    {
                        case "RECEIVER_JOINED":
                            _receiverJoinedTcs.TrySetResult(true);
                            break;

                        case "SIGNAL_DATA":
                            string payload = doc.RootElement.TryGetProperty("payload", out JsonElement payloadProp)
                                ? payloadProp.GetString() ?? ""
                                : "";
                            OnSignalDataReceived?.Invoke(payload);
                            break;
                    }
                }
            }
        }
        catch (Exception)
        {
            // Gestione chiusura impropria o disconnessione
        }
    }

    private async Task SendTextAsync(string text, CancellationToken ct)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await _webSocket!.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, ct);
    }

    private async Task<string> ReceiveTextAsync(CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 4];
        WebSocketReceiveResult result = await _webSocket!.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }

    public void Dispose()
    {
        _webSocket?.Dispose();
    }
}
