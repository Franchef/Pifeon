namespace Pifeon.Core.Abstractions;

public interface ISender : IAsyncDisposable
{
    string Code { get; }
    bool IsExpired { get; }

    event Action? OnReceiverJoined;

    /// <summary>
    /// Reports cumulative bytes sent, total bytes, and the current relative file path.
    /// </summary>
    event Action<long, long, string>? OnProgressChanged;

    /// <summary>
    /// Reports per-file and overall transfer progress. FileIndex is one-based.
    /// </summary>
    event Action<TransferProgress>? OnTransferProgressChanged;

    /// <summary>
    /// Inizializza la sessione sul server di segnalazione e restituisce il codice temporaneo.
    /// </summary>
    Task<string> InitializeSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts the send. 'sourcePath' can be either the path of a single file or an entire folder.
    /// </summary>
    Task SendAsync(string sourcePath, CancellationToken ct = default);
}
