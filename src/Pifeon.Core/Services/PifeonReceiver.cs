using Pifeon.Core.Abstractions;
using Pifeon.Core.Signaling;

namespace Pifeon.Core.Services;

public class PifeonReceiver : IReceiver
{
    private readonly WebSocketSignalingService _signalingService;
    private bool _disposed;

    public event Action<long, long, string>? OnProgressChanged;

    public PifeonReceiver(WebSocketSignalingService signalingService)
    {
        _signalingService = signalingService;
    }

    public async Task<TransferManifest> ConnectAndGetManifestAsync(string code, CancellationToken ct = default)
    {
        await _signalingService.JoinSessionAsync(code, ct);

        // Ricezione via WebSocket/P2P dei metadati del file/cartella
        // (Esempio dummy di ritorno manifest)
        return new TransferManifest(1, 0, Array.Empty<TransferItemInfo>());
    }

    public async Task ReceiveToDirectoryAsync(string destinationDirectory, CancellationToken ct = default)
    {
        // 1. Creazione delle sotto-cartelle e apertura dei file in scrittura (FileChunkWriter)
        // 2. Ricezione dei chunk, decifrazione AES-GCM e scrittura su disco
        // 3. Notifica dei progressi tramite OnProgressChanged
        await Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _signalingService.Dispose();
            _disposed = true;
        }
        await Task.CompletedTask;
    }
}
