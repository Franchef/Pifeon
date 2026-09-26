namespace Pifeon.Core.Abstractions;

public interface IPifeonServer
{
    /// <summary>
    /// L'URL corrente del server di segnalazione risolto o impostato manualmente.
    /// </summary>
    string ServerUrl { get; set; }

    /// <summary>
    /// Verifica se il server di segnalazione è raggiungibile ed è operativo.
    /// </summary>
    Task<bool> IsHealthyAsync(CancellationToken ct = default);

    /// <summary>
    /// Crea una nuova sessione di trasferimento e restituisce il mittente pronto col codice.
    /// </summary>
    Task<ISender> CreateSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Si unisce a una sessione esistente tramite il codice a 6 cifre e restituisce il ricevitore.
    /// </summary>
    Task<IReceiver> JoinSessionAsync(string code, CancellationToken ct = default);
}
