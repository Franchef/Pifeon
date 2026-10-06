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
    /// <summary>
    /// Handles the Sender's WebSocket connection on the /ws/session/create route
    /// </summary>
    public static async Task HandleSenderAsync(
        WebSocket webSocket,
        IPairingManager<WebSocket> manager,
        CancellationToken ct)
    {
        // 1. The manager creates the session and returns the unique 6-digit code
        string code = await manager.CreateSessionAsync(webSocket, ct);

        // 2. We immediately send the generated code to the Sender
        byte[] response = JsonSerializer.SerializeToUtf8Bytes(
            new CodeCreatedResponse("CODE_CREATED", code),
            SignalingJsonContext.Default.CodeCreatedResponse);

        await webSocket.SendAsync(response, WebSocketMessageType.Text, true, ct);

        // 3. Retrieve the newly created session
        if (manager.TryGetSession(code, out PairingSession<WebSocket>? session) && session != null)
        {
            try
            {
                using CancellationTokenSource relayCts = CancellationTokenSource.CreateLinkedTokenSource(ct, session.TimeoutCts.Token);
                await RelayMessagesAsync(webSocket, null, session.ReceiverSendLock, session.SenderSendLock, relayCts.Token, session);
            }
            catch (OperationCanceledException)
            {
                // Handle timeout (e.g. 5 minutes)
            }
            finally
            {
                manager.RemoveSession(code);
            }
        }
    }

    /// <summary>
    /// Handles the Receiver's WebSocket connection on the /ws/session/join/{code} route
    /// </summary>
    public static async Task HandleReceiverAsync(
        string code,
        WebSocket webSocket,
        PairingSession<WebSocket> session,
        IPairingManager<WebSocket> manager,
        CancellationToken ct)
    {
        // Unblock the TaskCompletionSource waiting in the Sender's thread
        if (session.ReceiverConnected.TrySetResult(webSocket))
        {
            // Start relaying from the Receiver side towards the Sender
            await RelayMessagesAsync(webSocket, session.Sender, session.SenderSendLock, session.ReceiverSendLock, session.TimeoutCts.Token);
        }
    }

    /// <summary>
    /// Implements the message relay loop between the two sockets (Sender <-> Receiver)
    /// </summary>
    private static async Task RelayMessagesAsync(WebSocket localSocket, WebSocket? remoteSocket,
        SemaphoreSlim remoteSendLock, SemaphoreSlim localSendLock, CancellationToken ct,
        PairingSession<WebSocket>? pairingSession = null)
    {
        byte[] buffer = new byte[64 * 1024];
        var transport = new Pifeon.Core.Networking.WebSocketTransportChannel(localSocket, ownsWebSocket: false);
        Task<int>? pendingRead = null;
        using CancellationTokenSource receiveCts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        try
        {
            if (pairingSession is not null)
            {
                Task<WebSocket> receiverTask = pairingSession.ReceiverConnected.Task.WaitAsync(ct);
                var bufferedMessages = new List<byte[]>();
                pendingRead = transport.ReceiveAsync(buffer, receiveCts.Token).AsTask();
                while (!receiverTask.IsCompleted)
                {
                    if (await Task.WhenAny(receiverTask, pendingRead) == receiverTask)
                    {
                        break;
                    }
                    int count = await pendingRead;
                    if (count == 0)
                    {
                        return;
                    }
                    using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, count));
                    if (bufferedMessages.Count != 0)
                    {
                        throw new InvalidDataException("Only one endpoint announcement is allowed before pairing.");
                    }
                    bufferedMessages.Add(buffer.AsSpan(0, count).ToArray());
                    pendingRead = transport.ReceiveAsync(buffer, receiveCts.Token).AsTask();
                }
                remoteSocket = await receiverTask;
                byte[] joined = JsonSerializer.SerializeToUtf8Bytes(new ReceiverJoinedResponse("RECEIVER_JOINED"),
                    SignalingJsonContext.Default.ReceiverJoinedResponse);
                await SendSignalingAsync(localSocket, joined, localSendLock, ct);
                foreach (byte[] message in bufferedMessages)
                {
                    await SendSignalingAsync(remoteSocket, message, remoteSendLock, ct);
                }
            }
            ArgumentNullException.ThrowIfNull(remoteSocket);
            while (localSocket.State == WebSocketState.Open &&
                   remoteSocket.State == WebSocketState.Open &&
                   !ct.IsCancellationRequested)
            {
                int count = pendingRead is not null ? await pendingRead : await transport.ReceiveAsync(buffer, receiveCts.Token);
                pendingRead = null;
                if (count == 0)
                {
                    break;
                }

                using JsonDocument document = JsonDocument.Parse(buffer.AsMemory(0, count));
                await SendSignalingAsync(remoteSocket, buffer.AsMemory(0, count), remoteSendLock, ct);
            }
        }
        catch (OperationCanceledException)
        {
            // Controlled close or cancellation
        }
        catch (WebSocketException)
        {
            // Sudden disconnection of one of the two parties
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            System.Diagnostics.Trace.TraceWarning($"Rejected invalid signaling message: {ex.Message}");
            using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
            await localSendLock.WaitAsync(closeCts.Token);
            try
            {
                await localSocket.CloseOutputAsync(WebSocketCloseStatus.InvalidPayloadData, "Only bounded JSON signaling messages are allowed", closeCts.Token);
            }
            finally
            {
                localSendLock.Release();
            }
        }
        finally
        {
            await receiveCts.CancelAsync();
            if (pendingRead is not null)
            {
                try
                {
                    await pendingRead;
                }
                catch (OperationCanceledException) when (receiveCts.IsCancellationRequested)
                {
                    // The pairing timeout or disconnect canceled the outstanding receive.
                }
                catch (Exception ex) when (ex is WebSocketException or IOException or InvalidDataException)
                {
                    System.Diagnostics.Trace.TraceInformation($"Signaling receive ended during cleanup: {ex.Message}");
                }
            }
            if (localSocket.State is WebSocketState.Open or WebSocketState.CloseReceived or WebSocketState.CloseSent)
            {
                using var closeCts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await localSendLock.WaitAsync(closeCts.Token);
                try
                {
                    await localSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Signaling ended", closeCts.Token);
                }
                catch (Exception ex) when (ex is WebSocketException or IOException or OperationCanceledException)
                {
                    System.Diagnostics.Trace.TraceWarning($"Signaling close handshake did not complete: {ex.Message}");
                    localSocket.Abort();
                }
                finally
                {
                    localSendLock.Release();
                }
            }

        }
    }

    private static async Task SendSignalingAsync(WebSocket socket, ReadOnlyMemory<byte> payload, SemaphoreSlim sendLock, CancellationToken ct)
    {
        await sendLock.WaitAsync(ct);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, true, ct);
        }
        finally
        {
            sendLock.Release();
        }
    }
}
