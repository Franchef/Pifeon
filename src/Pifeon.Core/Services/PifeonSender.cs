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
    private readonly WebSocketSignalingService _signalingService;
    private bool _disposed;

    public string Code { get; private set; } = string.Empty;
    public bool IsExpired { get; private set; }

    public event Action? OnReceiverJoined;
    public event Action<long, long, string>? OnProgressChanged;

    public PifeonSender(WebSocketSignalingService signalingService)
    {
        _signalingService = signalingService;
        //_signalingService.OnReceiverJoined += () => OnReceiverJoined?.Invoke();
    }

    public async Task<string> InitializeSessionAsync(CancellationToken ct = default)
    {
        Code = await _signalingService.CreateSessionAsync(ct);
        return Code;
    }

    public async Task SendAsync(string sourcePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Code))
        {
            throw new InvalidOperationException("La sessione deve essere prima inizializzata invocando InitializeSessionAsync().");
        }

        // 1. Scansione del percorso (file singolo o cartella)
        var items = FolderScanner.ScanPath(sourcePath).ToList();
        long totalBytes = items.Sum(i => i.FileSize);

        // 2. Attesa del ricevitore sul canale di segnalazione WebSocket
        await _signalingService.WaitForReceiverAsync(ct);

        // 3. Generazione chiavi ECDH e rilevamento IP tramite STUN
        using var keyExchange = new KeyExchange();
        IPEndPoint? publicIp = await StunClient.GetPublicIPEndPointAsync(ct: ct);

        // 4. Avvio connessione socket P2P diretta e invio dati
        using var p2pManager = new P2PConnectionManager();
        // ... Logica di streaming dei chunk cifrati con AesGcmEncryption ed emettendo OnProgressChanged ...
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
