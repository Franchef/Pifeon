namespace Pifeon.Core.Abstractions;

public interface IReceiver : IAsyncDisposable
{
    event Action<long, long, string>? OnProgressChanged;

    /// <summary>
    /// Si connette al sender e riceve il manifest dei file in arrivo (singolo file o cartella).
    /// </summary>
    Task<TransferManifest> ConnectAndGetManifestAsync(string code, CancellationToken ct = default);

    /// <summary>
    /// Avvia il trasferimento P2P salvando i file e la struttura delle cartelle dentro 'destinationDirectory'.
    /// </summary>
    Task ReceiveToDirectoryAsync(string destinationDirectory, CancellationToken ct = default);
}
