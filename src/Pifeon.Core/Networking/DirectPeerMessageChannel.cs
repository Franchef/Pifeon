using Pifeon.Core.Abstractions;

namespace Pifeon.Core.Networking;

internal sealed class DirectPeerMessageChannel : IEstablishablePeerMessageChannel
{
    private PeerMessageChannel? _channel;
    private bool _ready;
    private bool _disposed;
    private int _establishmentStarted;

    public bool IsConnected => _ready && _channel?.IsConnected == true;

    public Task EstablishAsync(
        IServerMessageChannel signaling,
        string code,
        bool isSender,
        CancellationToken ct = default) =>
        EstablishAsync(signaling, code, isSender ? PeerRole.Sender : PeerRole.Receiver, ct);

    public async Task EstablishAsync(
        IServerMessageChannel signaling,
        string code,
        PeerRole role,
        CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Interlocked.Exchange(ref _establishmentStarted, 1) != 0)
        {
            throw new InvalidOperationException("Peer connection establishment has already started.");
        }
        try
        {
            _channel = await DirectPeerConnectionEstablisher.EstablishAsync(signaling, code, role, ct);
            _ready = true;
        }
        catch
        {
            await DisposeAsync();
            throw;
        }
    }

    public ValueTask SendMessageAsync(PeerNetworkMessage message, CancellationToken ct = default) =>
        ConnectedChannel.SendMessageAsync(message, ct);

    public ValueTask<PeerNetworkMessage> ReceiveMessageAsync(CancellationToken ct = default) =>
        ConnectedChannel.ReceiveMessageAsync(ct);

    private PeerMessageChannel ConnectedChannel => _ready && _channel is not null
        ? _channel
        : throw new InvalidOperationException("Direct peer connection is not established.");

    public Task CloseAsync(CancellationToken ct = default)
    {
        _ready = false;
        return _channel?.CloseAsync(ct) ?? Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        _disposed = true;
        _ready = false;
        if (_channel is not null)
        {
            await _channel.DisposeAsync();
        }
    }
}
