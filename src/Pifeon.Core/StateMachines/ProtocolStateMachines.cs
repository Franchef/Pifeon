namespace Pifeon.Core.StateMachines;

internal enum ConnectionState
{
    Created,
    WaitingForPeer,
    Establishing,
    Connected,
    Closing,
    Closed,
    Failed
}

internal sealed class ConnectionStateMachine
{
    private readonly object _sync = new();
    private ConnectionState _state = ConnectionState.Created;

    public ConnectionState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public bool IsConnected => State == ConnectionState.Connected;

    public void TransitionTo(ConnectionState next)
    {
        lock (_sync)
        {
            if (_state == next)
            {
                return;
            }

            if (!IsValidTransition(_state, next))
            {
                throw new InvalidOperationException($"Invalid connection state transition from {_state} to {next}.");
            }

            _state = next;
        }
    }

    public bool TryBeginClosing()
    {
        lock (_sync)
        {
            if (_state is ConnectionState.Closing or ConnectionState.Closed)
            {
                return false;
            }

            _state = ConnectionState.Closing;
            return true;
        }
    }

    public void Fail()
    {
        lock (_sync)
        {
            if (_state is not (ConnectionState.Closing or ConnectionState.Closed))
            {
                _state = ConnectionState.Failed;
            }
        }
    }

    private static bool IsValidTransition(ConnectionState current, ConnectionState next) =>
        (current, next) switch
        {
            (ConnectionState.Created, ConnectionState.WaitingForPeer or ConnectionState.Establishing or ConnectionState.Connected) => true,
            (ConnectionState.WaitingForPeer, ConnectionState.Establishing or ConnectionState.Connected) => true,
            (ConnectionState.Establishing, ConnectionState.Connected) => true,
            (ConnectionState.Closing, ConnectionState.Closed) => true,
            _ when next is ConnectionState.Closing or ConnectionState.Failed => true,
            _ => false
        };
}

internal enum TransferState
{
    Idle,
    Preparing,
    WaitingForConnection,
    WaitingForManifest,
    Ready,
    Transferring,
    Completed,
    Failed,
    Disposed
}

internal sealed class TransferStateMachine
{
    private readonly object _sync = new();
    private TransferState _state = TransferState.Idle;

    public TransferState State
    {
        get
        {
            lock (_sync)
            {
                return _state;
            }
        }
    }

    public void TransitionTo(TransferState next)
    {
        lock (_sync)
        {
            if (!IsValidTransition(_state, next))
            {
                throw new InvalidOperationException($"Invalid transfer state transition from {_state} to {next}.");
            }

            _state = next;
        }
    }

    public void Fail()
    {
        lock (_sync)
        {
            if (_state != TransferState.Disposed)
            {
                _state = TransferState.Failed;
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _state = TransferState.Disposed;
        }
    }

    private static bool IsValidTransition(TransferState current, TransferState next) =>
        (current, next) switch
        {
            (TransferState.Idle, TransferState.Preparing or TransferState.WaitingForManifest) => true,
            (TransferState.Preparing, TransferState.WaitingForConnection or TransferState.Transferring) => true,
            (TransferState.WaitingForConnection, TransferState.Transferring) => true,
            (TransferState.WaitingForManifest, TransferState.Ready) => true,
            (TransferState.Ready, TransferState.Transferring) => true,
            (TransferState.Transferring, TransferState.Completed) => true,
            _ => false
        };
}
