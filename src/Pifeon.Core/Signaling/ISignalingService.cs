using System;
using System.Collections.Generic;
using System.Text;

namespace Pifeon.Core.Signaling;

public interface ISignalingService
{
    /// <summary>
    /// Evento sollevato quando si riceve un messaggio di segnalazione dal peer remoto (SDP / ICE Candidates).
    /// </summary>
    event Action<string>? OnSignalDataReceived;

    /// <summary>
    /// Avvia la sessione lato Mittente e restituisce il codice a 6 cifre generato dal server.
    /// </summary>
    Task<string> CreateSessionAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Attende che il Ricevitore si connetta inserendo il codice a 6 cifre.
    /// </summary>
    Task WaitForReceiverAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Si connette a una sessione esistente lato Ricevitore usando il codice a 6 cifre.
    /// </summary>
    Task<bool> JoinSessionAsync(string code, CancellationToken cancellationToken = default);

    /// <summary>
    /// Invia dati di segnalazione P2P (es. ICE candidates o SDP offer/answer) alla controparte.
    /// </summary>
    Task SendSignalDataAsync(string payload, CancellationToken cancellationToken = default);

    /// <summary>
    /// Chiude la connessione con il server di segnalazione.
    /// </summary>
    Task DisconnectAsync(CancellationToken cancellationToken = default);
}
