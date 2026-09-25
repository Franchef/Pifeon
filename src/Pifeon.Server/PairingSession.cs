using System.Diagnostics.CodeAnalysis;
using System.Net.WebSockets;

namespace Pifeon.Server;

[ExcludeFromCodeCoverage]
public record PairingSession<TConnection>(
    string Code,
    TConnection Sender,
    TaskCompletionSource<TConnection> ReceiverConnected,
    CancellationTokenSource TimeoutCts);
