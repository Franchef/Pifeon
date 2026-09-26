using System;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Runtime.ConstrainedExecution;
using System.Runtime.Intrinsics.Arm;
using System.Text;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Signaling;

public interface ISignalingService : IAsyncDisposable, IDisposable
{
    /// <summary>
    /// Evento sollevato quando il ricevitore inserisce il codice e si unisce alla sessione.
    /// </summary>
    event Action? OnReceiverJoined;

    /// <summary>
    /// Evento sollevato quando arrivano dati di segnalazione P2P (es. payload SDP / ICE candidates).
    /// </summary>
    event Action<string>? OnSignalDataReceived;

    /// <summary>
    /// Apre la connessione WebSocket con il server di segnalazione.
    /// </summary>
    Task ConnectAsync(CancellationToken ct = default);

    /// <summary>
    /// Richiede al server la creazione di una nuova sessione e restituisce il codice a 6 cifre.
    /// </summary>
    Task<string> CreateSessionAsync(CancellationToken ct = default);

    /// <summary>
    /// Si unisce a una sessione esistente utilizzando il codice fornito dal mittente.
    /// </summary>
    Task JoinSessionAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Attende che il ricevitore si sia connesso alla sessione.
    /// </summary>
    Task WaitForReceiverAsync(CancellationToken ct = default);

    /// <summary>
    /// Invia un payload di segnalazione (es. chiave pubblica o coordinate di rete) al peer connesso.
    /// </summary>
    Task SendSignalDataAsync(string payload, CancellationToken ct = default);

    /// <summary>
    /// Chiude la connessione con il server di segnalazione.
    /// </summary>
    Task DisconnectAsync(CancellationToken ct = default);
}
