using Pifeon.Core.Networking;

namespace Pifeon.Core.Abstractions;

public interface IMessageChannel : IAsyncDisposable
{
    /// <summary>
    /// Statuto della connessione di rete sottostante.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Invia un messaggio/chunk fortemente tipizzato sul canale.
    /// </summary>
    ValueTask SendMessageAsync(NetworkMessage message, CancellationToken ct = default);

    /// <summary>
    /// Riceve il prossimo messaggio dal canale.
    /// </summary>
    ValueTask<NetworkMessage> ReceiveMessageAsync(CancellationToken ct = default);

    /// <summary>
    /// Chiude pulitamente il canale di messaggistica.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}
