using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling;
using Pifeon.Core.StateMachines;

namespace Pifeon.Core.Services;

internal sealed class PifeonSession : ISenderSession, IReceiverSession
{
    private readonly IServerMessageChannel _serverChannel;
    private readonly IPeerMessageChannel _peerChannel;
    private readonly bool _isSenderSession;
    private readonly ConnectionStateMachine _connectionState = new();
    private readonly TaskCompletionSource<bool> _connectedTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Task? _connectionTask;
    private readonly CancellationTokenSource _lifetimeCts = new(TimeSpan.FromMinutes(5));
    private bool _disposed;

    public string Code { get; private set; }

    public bool IsConnected => _connectionState.IsConnected;

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

        if (!isSenderSession && peerChannel is not IEstablishablePeerMessageChannel)
        {
            _connectionState.TransitionTo(ConnectionState.Connected);
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
            session.InitializeReceiver(ct);
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
        StartConnectionListener();
        await _connectedTcs.Task.WaitAsync(ct);
    }

    public Task<ISender> GetSenderAsync(CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (!_isSenderSession)
        {
            throw new InvalidOperationException("Sender is available only for a session created by the sender client.");
        }

        StartConnectionListener();
        return Task.FromResult<ISender>(new PifeonSender(_peerChannel, Code, this));
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
        if (!_connectionState.TryBeginClosing())
        {
            return;
        }

        await _lifetimeCts.CancelAsync();
        if (_connectionTask is not null)
        {
            await _connectionTask;
        }
        try
        {
            await _peerChannel.CloseAsync(ct);
        }
        finally
        {
            await _serverChannel.CloseAsync(ct);
            _connectionState.TransitionTo(ConnectionState.Closed);
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
        Code = SignalingProtocol.ParseCreatedCode(response);

        StartConnectionListener();
    }

    private void InitializeReceiver(CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        StartConnectionListener();
    }

    private void StartConnectionListener()
    {
        if (!_isSenderSession && _peerChannel is not IEstablishablePeerMessageChannel || _connectionTask is not null)
        {
            return;
        }

        _connectionState.TransitionTo(_isSenderSession
            ? ConnectionState.WaitingForPeer
            : ConnectionState.Establishing);

        _connectionTask = RunConnectionAsync();
    }

    private async Task RunConnectionAsync()
    {
        try
        {
            if (_peerChannel is IEstablishablePeerMessageChannel establishableChannel)
            {
                if (_connectionState.State == ConnectionState.WaitingForPeer)
                {
                    _connectionState.TransitionTo(ConnectionState.Establishing);
                }
                PeerRole role = _isSenderSession ? PeerRole.Sender : PeerRole.Receiver;
                await establishableChannel.EstablishAsync(_serverChannel, Code, role, _lifetimeCts.Token);
                await _serverChannel.CloseAsync(_lifetimeCts.Token);
                CompleteConnection();
                return;
            }

            ServerNetworkMessage message = await _serverChannel.ReceiveMessageAsync(_lifetimeCts.Token);
            SignalingProtocol.EnsureReceiverJoined(message, Code);
            CompleteConnection();
        }
        catch (Exception ex)
        {
            _connectionState.Fail();
            _connectedTcs.TrySetException(ex);
        }
    }

    private void CompleteConnection()
    {
        OnReceiverJoined?.Invoke();
        _connectionState.TransitionTo(ConnectionState.Connected);
        _connectedTcs.TrySetResult(true);
    }
}
