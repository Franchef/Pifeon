namespace Pifeon.Core.Abstractions;

public interface IPifeonServer : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// L'URL corrente del server di segnalazione risolto o impostato manualmente.
    /// </summary>
    string ServerUrl { get; }

    /// <summary>
    /// Verifica se il server di segnalazione è raggiungibile ed è operativo.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);

    /// <summary>
    /// Crea una sessione di pairing e restituisce l'handle di sessione.
    /// </summary>
    Task<ISenderSession> CreateSessionHandleAsync(CancellationToken ct = default);

    /// <summary>
    /// Si unisce a una sessione esistente e restituisce l'handle di sessione.
    /// </summary>
    Task<IReceiverSession> JoinSessionHandleAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Crea una nuova sessione di trasferimento e restituisce il mittente pronto col codice.
    /// API di compatibilità: preferire CreateSessionHandleAsync.
    /// </summary>
    Task<ISender> CreateSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Si unisce a una sessione esistente tramite il codice a 6 cifre e restituisce il ricevitore.
    /// API di compatibilità: preferire JoinSessionHandleAsync.
    /// </summary>
    Task<IReceiver> JoinSessionAsync(string code, CancellationToken ct = default);
}
