namespace Pifeon.Core.Abstractions;

public interface ISession : IAsyncDisposable
{
    string Code { get; }
    bool IsConnected { get; }

    Task WaitUntilConnectedAsync(CancellationToken ct = default);
    Task<ISender> GetSenderAsync(CancellationToken ct = default);
    Task<IReceiver> GetReceiverAsync(CancellationToken ct = default);
    Task CloseAsync(CancellationToken ct = default);
}
