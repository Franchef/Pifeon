using Pifeon.Core.Signaling.Models;

namespace Pifeon.Core.Abstractions;

public interface IPairingManager<TConnection>
{
    /// <summary>
    /// Crea una nuova sessione generandone il codice unico.
    /// </summary>
    Task<string> CreateSessionAsync(TConnection senderConnection, CancellationToken ct = default);

    /// <summary>
    /// Tenta di unire un receiver a una sessione esistente usando il codice.
    /// </summary>
    Task<bool> TryJoinSessionAsync(string code, TConnection receiverConnection, CancellationToken ct = default);

    /// <summary>
    /// Recupera una sessione se presente.
    /// </summary>
    bool TryGetSession(string code, out PairingSession<TConnection>? session);

    /// <summary>
    /// Rimuove una sessione e rilascia le risorse allocate.
    /// </summary>
    void RemoveSession(string code);
}
