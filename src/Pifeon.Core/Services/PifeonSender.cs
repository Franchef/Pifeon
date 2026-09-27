using System;
using System.Collections.Generic;
using System.Net;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Cryptography;
using Pifeon.Core.IO;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling;

namespace Pifeon.Core.Services;

public class PifeonSender : ISender
{
    private readonly ISignalingService _signalingService;
    private readonly ITransportChannel _dataChannel;
    private bool _disposed;

    public string Code { get; private set; } = string.Empty;
    public bool IsExpired { get; private set; }

    public event Action? OnReceiverJoined;
    public event Action<long, long, string>? OnProgressChanged;

    public PifeonSender(ISignalingService signalingService, ITransportChannel dataChannel)
    {
        _signalingService = signalingService ?? throw new ArgumentNullException(nameof(signalingService));
        _dataChannel = dataChannel ?? throw new ArgumentNullException(nameof(dataChannel));

        _signalingService.OnReceiverJoined += () => OnReceiverJoined?.Invoke();
    }

    public async Task<string> InitializeSessionAsync(CancellationToken ct = default)
    {
        Code = await _signalingService.CreateSessionAsync(ct);
        return Code;
    }

    public async Task SendAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!string.IsNullOrEmpty(Code))
        {
            // 1. Scansione del percorso locale (file o cartella con calcolo SHA-256)
            List<TransferItem> items = await FolderScanner.ScanPath(sourcePath).ToListAsync(ct);
            long totalBytes = items.Sum(i => i.FileSize);

            // 2. Attesa della connessione del ricevitore via segnalazione
            await _signalingService.WaitForReceiverAsync(ct);
        }
        else
        {
            throw new InvalidOperationException("Invocare prima InitializeSessionAsync().");
        }

        // 3. Invio del Manifest e streaming dei dati sul canale di comunicazione astratto
        // ... Logica di invio dei chunk tramite _dataChannel.SendAsync(...)
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
