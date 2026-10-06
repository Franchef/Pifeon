using System.Text;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Exceptions;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Services;

public sealed class PifeonSession : ISenderSession, IReceiverSession
{
    private readonly IServerMessageChannel _serverChannel;
    private readonly IPeerMessageChannel _peerChannel;
    private readonly bool _isSenderSession;
    private readonly TaskCompletionSource<bool> _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _senderConnectionListenerTask;
    private readonly CancellationTokenSource _lifetimeCts = new(TimeSpan.FromMinutes(5));
    private bool _disposed;
    private bool _closed;

    public string Code { get; private set; }

    public bool IsConnected => !_closed && _connectedTcs.Task.IsCompletedSuccessfully;

    public event Action? OnReceiverJoined;

    private PifeonSession(
        IServerMessageChannel serverChannel,
        IPeerMessageChannel peerChannel,
        bool isSenderSession,
        string code)
    {
        _serverChannel = serverChannel;
        _peerChannel = peerChannel;
        _isSenderSession = isSenderSession;
        Code = code;

        if (!isSenderSession && peerChannel is not DirectPeerMessageChannel)
        {
            _connectedTcs.TrySetResult(true);
        }
    }

    public static async Task<ISenderSession> CreateSenderAsync(
        IServerMessageChannel serverChannel,
        IPeerMessageChannel peerChannel,
        CancellationToken ct = default)
    {
        var session = new PifeonSession(serverChannel, peerChannel, isSenderSession: true, code: string.Empty);
        try
        {
            await session.InitializeSenderAsync(ct);
            return session;
        }
        catch
        {
            session._lifetimeCts.Dispose();
            throw;
        }
    }

    public static async Task<IReceiverSession> CreateReceiverAsync(
        string code,
        IServerMessageChannel serverChannel,
        IPeerMessageChannel peerChannel,
        CancellationToken ct = default)
    {
        var session = new PifeonSession(serverChannel, peerChannel, isSenderSession: false, code);
        try
        {
            await session.InitializeReceiverAsync(code, ct);
            return session;
        }
        catch
        {
            session._lifetimeCts.Dispose();
            throw;
        }
    }

    public async Task WaitUntilConnectedAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isSenderSession)
        {
            StartSenderConnectionListener();
            await _connectedTcs.Task.WaitAsync(ct);
            return;
        }

        if (_peerChannel is DirectPeerMessageChannel)
        {
            await _connectedTcs.Task.WaitAsync(ct);
            return;
        }

        if (IsConnected)
        {
            return;
        }

        while (!IsConnected)
        {
            ServerNetworkMessage message = await _serverChannel.ReceiveMessageAsync(ct);
            HandleServerMessage(message);
        }

        await _connectedTcs.Task.WaitAsync(ct);
    }

    public async Task<ISender> GetSenderAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isSenderSession)
        {
            throw new InvalidOperationException("Sender is available only for a session created by the sender client.");
        }

        StartSenderConnectionListener();
        await Task.CompletedTask;
        return new PifeonSender(_peerChannel, Code, this);
    }

    public async Task<IReceiver> GetReceiverAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_isSenderSession)
        {
            throw new InvalidOperationException("Receiver is available only for a session joined by the receiver client.");
        }

        await WaitUntilConnectedAsync(ct);
        return new PifeonReceiver(_peerChannel, this);
    }

    public async Task CloseAsync(CancellationToken ct = default)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;

        await _lifetimeCts.CancelAsync();
        if (_senderConnectionListenerTask is not null)
        {
            await _senderConnectionListenerTask;
        }
        try
        {
            await _peerChannel.CloseAsync(ct);
        }
        finally
        {
            await _serverChannel.CloseAsync(ct);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await _lifetimeCts.CancelAsync();

        using CancellationTokenSource cleanupCts = new(TimeSpan.FromSeconds(5));
        try
        {
            await CloseAsync(cleanupCts.Token);
        }
        finally
        {
            try
            {
                await _peerChannel.DisposeAsync();
            }
            finally
            {
                await _serverChannel.DisposeAsync();
                _lifetimeCts.Dispose();
            }
        }
    }

    private async Task InitializeSenderAsync(CancellationToken ct)
    {
        ServerNetworkMessage response = await _serverChannel.ReceiveMessageAsync(ct);
        Code = ParseCodeCreatedResponse(response);

        StartSenderConnectionListener();
    }

    private Task InitializeReceiverAsync(string code, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        StartSenderConnectionListener();
        return Task.CompletedTask;
    }

    private void StartSenderConnectionListener()
    {
        if ((!_isSenderSession && _peerChannel is not DirectPeerMessageChannel) || _senderConnectionListenerTask is not null)
        {
            return;
        }

        _senderConnectionListenerTask = Task.Run(async () =>
        {
            try
            {
                if (_peerChannel is DirectPeerMessageChannel directChannel)
                {
                    await directChannel.EstablishAsync(_serverChannel, Code, _isSenderSession, _lifetimeCts.Token);
                    await _serverChannel.CloseAsync(_lifetimeCts.Token);
                    OnReceiverJoined?.Invoke();
                    _connectedTcs.TrySetResult(true);
                    return;
                }
                while (!_connectedTcs.Task.IsCompleted)
                {
                    ServerNetworkMessage message = await _serverChannel.ReceiveMessageAsync(_lifetimeCts.Token);
                    HandleServerMessage(message);
                }
            }
            catch (Exception ex)
            {
                _connectedTcs.TrySetException(ex);
            }
        });
    }

    private void HandleServerMessage(ServerNetworkMessage message)
    {
        if (message.Type == ServerNetworkMessageType.Abort)
        {
            ThrowFromErrorPayload(message.Payload.Span, Code);
        }

        string? type = TryReadMessageType(message.Payload.Span);

        if (string.Equals(type, "RECEIVER_JOINED", StringComparison.OrdinalIgnoreCase)
            || message.Type == ServerNetworkMessageType.IpExchanges)
        {
            // Fire the receiver joined event before marking as connected
            OnReceiverJoined?.Invoke();
            _connectedTcs.TrySetResult(true);
            return;
        }

        if (string.Equals(type, "ERROR", StringComparison.OrdinalIgnoreCase))
        {
            ThrowFromErrorPayload(message.Payload.Span, Code);
        }
        throw new InvalidDataException($"Unexpected signaling message: {type}.");
    }

    private static string ParseCodeCreatedResponse(ServerNetworkMessage message)
    {
        string? type = TryReadMessageType(message.Payload.Span);

        if (!string.Equals(type, "CODE_CREATED", StringComparison.OrdinalIgnoreCase))
        {
            if (string.Equals(type, "ERROR", StringComparison.OrdinalIgnoreCase) || message.Type == ServerNetworkMessageType.Abort)
            {
                ThrowFromErrorPayload(message.Payload.Span, code: string.Empty);
            }

            throw new InvalidOperationException("Unexpected signaling response while creating session.");
        }

        CodeCreatedResponse? response = JsonSerializer.Deserialize(
            message.Payload.Span,
            SignalingJsonContext.Default.CodeCreatedResponse);

        if (response is null || string.IsNullOrWhiteSpace(response.Code))
        {
            throw new InvalidOperationException("Signaling server returned an invalid session code payload.");
        }

        return response.Code;
    }

    private static string? TryReadMessageType(ReadOnlySpan<byte> payload)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(payload.ToArray());
            return document.RootElement.TryGetProperty("type", out JsonElement typeElement)
                ? typeElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static void ThrowFromErrorPayload(ReadOnlySpan<byte> payload, string code)
    {
        ErrorResponse? error = null;

        try
        {
            error = JsonSerializer.Deserialize(payload, SignalingJsonContext.Default.ErrorResponse);
        }
        catch (JsonException)
        {
            // Ignore malformed payload and fallback to generic exception.
        }

        string message = error?.Message ?? Encoding.UTF8.GetString(payload);

        if (message.Contains("rate", StringComparison.OrdinalIgnoreCase)
            || message.Contains("too many", StringComparison.OrdinalIgnoreCase)
            || message.Contains("429", StringComparison.OrdinalIgnoreCase))
        {
            throw new CreateSessionRateLimitedException(message);
        }

        if (message.Contains("not found", StringComparison.OrdinalIgnoreCase)
            || message.Contains("invalid", StringComparison.OrdinalIgnoreCase)
            || message.Contains("expired", StringComparison.OrdinalIgnoreCase)
            || message.Contains("404", StringComparison.OrdinalIgnoreCase))
        {
            throw new SessionNotFoundException(code);
        }

        throw new InvalidOperationException(string.IsNullOrWhiteSpace(message)
            ? "Signaling server returned an error."
            : message);
    }
}
