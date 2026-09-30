using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling.Messages;
using Pifeon.Core.Signaling.Models;

namespace Pifeon.Server;

public static class WebSocketHandler
{
    //public static async Task HandlePairingAsync(HttpContext context, IPairingManager<WebSocket> manager, CancellationToken cancellationToken)
    //{
    //    if (!context.WebSockets.IsWebSocketRequest)
    //    {
    //        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    //        return;
    //    }

    //    using WebSocket webSocket = await context.WebSockets.AcceptWebSocketAsync();
    //    byte[] buffer = new byte[1024 * 4];

    //    // 1. Legge il primo messaggio per determinare l'azione (CREATE o JOIN)
    //    WebSocketReceiveResult result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
    //    if (result.MessageType != WebSocketMessageType.Text)
    //    {
    //        return;
    //    }

    //    string jsonText = Encoding.UTF8.GetString(buffer, 0, result.Count);
    //    using var doc = JsonDocument.Parse(jsonText);
    //    JsonElement root = doc.RootElement;

    //    string action = root.GetProperty("action").GetString() ?? "";

    //    if (action == "CREATE")
    //    {
    //        // === SENDER ===
    //        // Il manager garantisce l'univocità del codice a 6 cifre
    //        string code = await manager.CreateSessionAsync(webSocket, cancellationToken);

    //        // Invia il codice generato al Sender
    //        byte[] response = JsonSerializer.SerializeToUtf8Bytes(new CodeCreatedResponse("CODE_CREATED", code), SignalingJsonContext.Default.CodeCreatedResponse);
    //        await webSocket.SendAsync(response, WebSocketMessageType.Text, true, CancellationToken.None);

    //        if (manager.TryGetSession(code, out PairingSession<WebSocket>? session) && session != null)
    //        {
    //            try
    //            {
    //                // Attende che il Receiver si connetta inserendo il codice (o timeout di 5 minuti)
    //                WebSocket receiverSocket = await session.ReceiverConnected.Task.WaitAsync(session.TimeoutCts.Token);

    //                // Notifica il Sender che la controparte è agganciata
    //                byte[] connectedMsg = JsonSerializer.SerializeToUtf8Bytes(new ReceiverJoinedResponse("RECEIVER_JOINED"), SignalingJsonContext.Default.ReceiverJoinedResponse);
    //                await webSocket.SendAsync(connectedMsg, WebSocketMessageType.Text, true, CancellationToken.None);

    //                // Avvia il relay trasparente dei pacchetti SDP / ICE candidates
    //                await RelayMessagesAsync(webSocket, receiverSocket, session.TimeoutCts.Token);
    //            }
    //            catch (OperationCanceledException)
    //            {
    //                // Gestione scadenza tempo (timeout 5 minuti)
    //            }
    //            finally
    //            {
    //                manager.RemoveSession(code);
    //            }
    //        }
    //    }
    //    else if (action == "JOIN")
    //    {
    //        // === RECEIVER ===
    //        string code = root.GetProperty("code").GetString() ?? "";

    //        if (manager.TryGetSession(code, out PairingSession<WebSocket>? session) && session != null)
    //        {
    //            // Sblocca il task in attesa nel thread del Sender
    //            session.ReceiverConnected.TrySetResult(webSocket);

    //            // Avvia il relay dal lato Receiver verso il Sender
    //            await RelayMessagesAsync(webSocket, session.Sender, session.TimeoutCts.Token);
    //        }
    //        else
    //        {
    //            // Errore: codice inesistente o scaduto
    //            byte[] errorMsg = JsonSerializer.SerializeToUtf8Bytes(new ErrorResponse("ERROR", "Code invalid or expired"), SignalingJsonContext.Default.ErrorResponse);
    //            await webSocket.SendAsync(errorMsg, WebSocketMessageType.Text, true, CancellationToken.None);
    //        }
    //    }
    //}

    //private static async Task RelayMessagesAsync(WebSocket localSocket, WebSocket remoteSocket, CancellationToken ct)
    //{
    //    byte[] buffer = new byte[1024 * 8];

    //    while (localSocket.State == WebSocketState.Open && remoteSocket.State == WebSocketState.Open && !ct.IsCancellationRequested)
    //    {
    //        WebSocketReceiveResult result = await localSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
    //        if (result.MessageType == WebSocketMessageType.Close)
    //        {
    //            break;
    //        }

    //        await remoteSocket.SendAsync(
    //            new ArraySegment<byte>(buffer, 0, result.Count),
    //            result.MessageType,
    //            result.EndOfMessage,
    //            ct);
    //    }
    //}

    /// <summary>
    /// Gestisce la connessione WebSocket del Sender sulla rotta /ws/session/create
    /// </summary>
    public static async Task HandleSenderAsync(
        WebSocket webSocket,
        IPairingManager<WebSocket> manager,
        CancellationToken ct)
    {
        // 1. Il manager crea la sessione e restituisce il codice univoco a 6 cifre
        string code = await manager.CreateSessionAsync(webSocket, ct);

        // 2. Inviamo subito il codice generato al Sender
        byte[] response = JsonSerializer.SerializeToUtf8Bytes(
            new CodeCreatedResponse("CODE_CREATED", code),
            SignalingJsonContext.Default.CodeCreatedResponse);

        await webSocket.SendAsync(response, WebSocketMessageType.Text, true, ct);

        // 3. Recuperiamo la sessione appena creata
        if (manager.TryGetSession(code, out PairingSession<WebSocket>? session) && session != null)
        {
            try
            {
                // Attende che il Receiver si connetta tramite la rotta /ws/session/join/{code} (o timeout)
                WebSocket receiverSocket = await session.ReceiverConnected.Task.WaitAsync(session.TimeoutCts.Token);

                // Notifica al Sender che il Receiver si è agganciato
                byte[] connectedMsg = JsonSerializer.SerializeToUtf8Bytes(
                    new ReceiverJoinedResponse("RECEIVER_JOINED"),
                    SignalingJsonContext.Default.ReceiverJoinedResponse);

                await webSocket.SendAsync(connectedMsg, WebSocketMessageType.Text, true, ct);

                // Avvia il relay bidirezionale dei messaggi (SDP, Candidate ICE, Endpoints P2P)
                await RelayMessagesAsync(webSocket, receiverSocket, session.TimeoutCts.Token);
            }
            catch (OperationCanceledException)
            {
                // Gestione del timeout (es. 5 minuti)
            }
            finally
            {
                manager.RemoveSession(code);
            }
        }
    }

    /// <summary>
    /// Gestisce la connessione WebSocket del Receiver sulla rotta /ws/session/join/{code}
    /// </summary>
    public static async Task HandleReceiverAsync(
        string code,
        WebSocket webSocket,
        PairingSession<WebSocket> session,
        IPairingManager<WebSocket> manager,
        CancellationToken ct)
    {
        // Sblocca il TaskCompletionSource in attesa nel thread del Sender
        if (session.ReceiverConnected.TrySetResult(webSocket))
        {
            // Avvia il relay dal lato Receiver verso il Sender
            await RelayMessagesAsync(webSocket, session.Sender, session.TimeoutCts.Token);
        }
    }

    /// <summary>
    /// Inserisce il loop di relay dei messaggi tra i due socket (Sender <-> Receiver)
    /// </summary>
    private static async Task RelayMessagesAsync(WebSocket localSocket, WebSocket remoteSocket, CancellationToken ct)
    {
        byte[] buffer = new byte[1024 * 8];

        try
        {
            while (localSocket.State == WebSocketState.Open &&
                   remoteSocket.State == WebSocketState.Open &&
                   !ct.IsCancellationRequested)
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
        catch (OperationCanceledException)
        {
            // Chiusura o annullamento controllato
        }
        catch (WebSocketException)
        {
            // Disconnessione improvvisa di una delle due parti
        }
    }
}
