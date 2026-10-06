namespace Pifeon.Core.Abstractions;

public interface ISender : IAsyncDisposable
{
    string Code { get; }
    bool IsExpired { get; }

    event Action? OnReceiverJoined;

    /// <summary>
    /// Notifica il progresso: (byte inviati, byte totali, file corrente in corso)
    /// </summary>
    event Action<long, long, string>? OnProgressChanged;

    /// <summary>
    /// Inizializza la sessione sul server di segnalazione e restituisce il codice a 6 cifre.
    /// </summary>
    Task<string> InitializeSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Starts the send. 'sourcePath' can be either the path of a single file or an entire folder.
    /// </summary>
    Task SendAsync(string sourcePath, CancellationToken ct = default);
}
