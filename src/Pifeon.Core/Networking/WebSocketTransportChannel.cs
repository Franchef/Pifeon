using System;
using System.Collections.Generic;
using System.Net.WebSockets;
using System.Text;
using Pifeon.Core.Abstractions;

namespace Pifeon.Core.Networking;

public sealed class WebSocketTransportChannel : ITransportChannel
{
    private readonly ClientWebSocket _webSocket;
    private readonly bool _ownsWebSocket;
    private bool _disposed;

    public bool IsConnected => _webSocket.State == WebSocketState.Open;

    /// <summary>
    /// Costruttore che accetta un'istanza esistente di ClientWebSocket o ne crea una nuova.
    /// </summary>
    public WebSocketTransportChannel(ClientWebSocket webSocket, bool ownsWebSocket = true)
    {
        _webSocket = webSocket ?? throw new ArgumentNullException(nameof(webSocket));
        _ownsWebSocket = ownsWebSocket;
    }

    /// <summary>
    /// Crea e connette un nuovo canale WebSocket a partire dall'URL specificato.
    /// </summary>
    public static async Task<WebSocketTransportChannel> ConnectAsync(string serverUrl, CancellationToken ct = default)
    {
        var webSocket = new ClientWebSocket();
        try
        {
            await webSocket.ConnectAsync(new Uri(serverUrl), ct);
            return new WebSocketTransportChannel(webSocket, ownsWebSocket: true);
        }
        catch
        {
            webSocket.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Invia un frame binario sul socket WebSocket.
    /// </summary>
    public async ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default)
    {
        EnsureConnected();

        await _webSocket.SendAsync(
            data,
            WebSocketMessageType.Binary,
            endOfMessage: true,
            cancellationToken: ct);
    }

    /// <summary>
    /// Riceve dati dal WebSocket riempiendo il buffer passato.
    /// Gestisce la ricezione di messaggi frame per frame o parziali.
    /// </summary>
    public async ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        EnsureConnected();

        ValueWebSocketReceiveResult result = await _webSocket.ReceiveAsync(buffer, ct);

        // Se il server richiede la chiusura della connessione
        if (result.MessageType == WebSocketMessageType.Close)
        {
            await CloseAsync(ct);
            return 0;
        }

        return result.Count;
    }

    /// <summary>
    /// Esegue il handshake di chiusura del WebSocket.
    /// </summary>
    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (_webSocket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _webSocket.CloseAsync(
                WebSocketCloseStatus.NormalClosure,
                "Chiusura del canale richiesta",
                ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;

            if (_ownsWebSocket)
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    try
                    {
                        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                        await CloseAsync(cts.Token);
                    }
                    catch
                    {
                        // In fase di dispose ignoriamo eventuali eccezioni di chiusura forzata
                    }
                }

                _webSocket.Dispose();
            }
        }
    }

    private void EnsureConnected()
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException($"Il canale WebSocket non è connesso. Stato attuale: {_webSocket.State}");
        }
    }
}
