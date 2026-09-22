using System.Net.WebSockets;

namespace Pifeon.Server;

public record PairingSession<TConnection>(
    string Code,
    TConnection Sender,
    TaskCompletionSource<TConnection> ReceiverConnected,
    CancellationTokenSource TimeoutCts);
