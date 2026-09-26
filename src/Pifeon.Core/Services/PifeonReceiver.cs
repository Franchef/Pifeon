using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling;

namespace Pifeon.Core.Services;

public class PifeonReceiver : IReceiver
{
    private readonly ISignalingService _signalingService;
    private readonly ITransportChannel _dataChannel;
    private bool _disposed;

    public event Action<long, long, string>? OnProgressChanged;

    public PifeonReceiver(ISignalingService signalingService, ITransportChannel dataChannel)
    {
        _signalingService = signalingService ?? throw new ArgumentNullException(nameof(signalingService));
        _dataChannel = dataChannel ?? throw new ArgumentNullException(nameof(dataChannel));
    }

    public async Task<TransferManifest> ConnectAndGetManifestAsync(string code, CancellationToken ct = default)
    {
        await _signalingService.JoinSessionAsync(code, ct);

        // Ricezione del manifest inviato dal mittente tramite il canale astratto
        // ... Lettura iniziale via _dataChannel.ReceiveAsync(...)

        return new TransferManifest(0, 0, []);
    }

    public async Task ReceiveToDirectoryAsync(string destinationDirectory, CancellationToken ct = default)
    {
        // 1. Ricezione dei singoli chunk dei file via _dataChannel.ReceiveAsync(...)
        // 2. Scrittura su disco e verifica dell'hash SHA-256 finale
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _dataChannel.DisposeAsync();
            await _signalingService.DisposeAsync();
        }
    }
}
