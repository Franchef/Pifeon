using System;
using System.Collections.Generic;
using System.Text;

namespace Pifeon.Core.Abstractions;

public interface ITransportChannel : IAsyncDisposable
{
    /// <summary>
    /// Statuto della connessione del canale.
    /// </summary>
    bool IsConnected { get; }

    /// <summary>
    /// Invio di un blocco di dati sul canale di comunicazione.
    /// </summary>
    ValueTask SendAsync(ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// Ricezione di un blocco di dati dal canale.
    /// </summary>
    ValueTask<int> ReceiveAsync(Memory<byte> buffer, CancellationToken ct = default);

    /// <summary>
    /// Chiude la connessione ed effettua il teardown delle risorse di rete.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}
