using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Server;

public static class WebSocketHandler
{
    public static async Task HandlePairingAsync(HttpContext context, PairingManager<WebSocket> manager)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
        byte[] buffer = new byte[1024 * 4];

        // 1. Legge il primo messaggio per determinare l'azione (CREATE o JOIN)
        WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
        if (result.MessageType != WebSocketMessageType.Text)
        {
            return;
        }

        string jsonText = Encoding.UTF8.GetString(buffer, 0, result.Count);
        using var doc = JsonDocument.Parse(jsonText);
        JsonElement root = doc.RootElement;

        string action = root.GetProperty("action").GetString() ?? "";

        if (action == "CREATE")
        {
            // === SENDER ===
            // Il manager garantisce l'univocità del codice a 6 cifre
            string code = manager.CreateSession(webSocket);

            // Invia il codice generato al Sender
            byte[] response = JsonSerializer.SerializeToUtf8Bytes(new CodeCreatedResponse("CODE_CREATED", code), SignalingJsonContext.Default.CodeCreatedResponse);
            await webSocket.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);

            if (manager.TryGetSession(code, out PairingSession<WebSocket>? session) && session != null)
            {
                try
                {
                    // Attende che il Receiver si connetta inserendo il codice (o timeout di 5 minuti)
                    WebSocket receiverSocket = await session.ReceiverConnected.Task.WaitAsync(session.TimeoutCts.Token);

                    // Notifica il Sender che la controparte è agganciata
                    byte[] connectedMsg = JsonSerializer.SerializeToUtf8Bytes(new ReceiverJoinedResponse("RECEIVER_JOINED"), SignalingJsonContext.Default.ReceiverJoinedResponse);
                    await webSocket.SendAsync(connectedMsg, WebSocketMessageType.Text, true, CancellationToken.None);

                    // Avvia il relay trasparente dei pacchetti SDP / ICE candidates
                    await RelayMessagesAsync(webSocket, receiverSocket, session.TimeoutCts.Token);
                }
                catch (OperationCanceledException)
                {
                    // Gestione scadenza tempo (timeout 5 minuti)
                }
                finally
                {
                    manager.RemoveSession(code);
                }
            }
        }
        else if (action == "JOIN")
        {
            // === RECEIVER ===
            string code = root.GetProperty("code").GetString() ?? "";

            if (manager.TryGetSession(code, out PairingSession<WebSocket>? session) && session != null)
            {
                // Sblocca il task in attesa nel thread del Sender
                session.ReceiverConnected.TrySetResult(webSocket);

                // Avvia il relay dal lato Receiver verso il Sender
                await RelayMessagesAsync(webSocket, session.Sender, session.TimeoutCts.Token);
            }
            else
            {
                // Errore: codice inesistente o scaduto
                byte[] errorMsg = JsonSerializer.SerializeToUtf8Bytes(new ErrorResponse("ERROR", "Code invalid or expired"), SignalingJsonContext.Default.ErrorResponse);
                await webSocket.SendAsync(errorMsg, WebSocketMessageType.Text, true, CancellationToken.None);
            }
        }
    }

    private static async Task RelayMessagesAsync(WebSocket localSocket, WebSocket remoteSocket, CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 8];

        while (localSocket.State == WebSocketState.Open && remoteSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            WebSocketReceiveResult result = await localSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                break;
            }

            await remoteSocket.SendAsync(
                new ArraySegment<byte>(buffer, 0, result.Count),
                result.MessageType,
                result.EndOfMessage,
                ct);
        }
    }
}
