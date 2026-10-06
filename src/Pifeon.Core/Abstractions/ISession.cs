namespace Pifeon.Core.Abstractions;

public interface ISession : IAsyncDisposable
{
    string Code { get; }
    /// <summary>
    /// Indicates a confirmed peer connection; production sessions complete encrypted key confirmation first.
    /// </summary>
    bool IsConnected { get; }
    Task WaitUntilConnectedAsync(CancellationToken ct = default);
    /// <summary>
    /// Closes the peer connection and any remaining signaling connection.
    /// Signaling is closed automatically after the direct peer handshake, without closing the peer.
    /// </summary>
    Task CloseAsync(CancellationToken ct = default);
}

public interface ISenderSession : ISession
{
    Task<ISender> GetSenderAsync(CancellationToken ct = default);
}

public interface IReceiverSession : ISession
{
    Task<IReceiver> GetReceiverAsync(CancellationToken ct = default);
}
