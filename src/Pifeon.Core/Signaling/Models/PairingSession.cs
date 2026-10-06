using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Pifeon.Core.Signaling.Models;

[ExcludeFromCodeCoverage]
public sealed record PairingSession<TConnection>(
    string Code,
    TConnection Sender,
    TaskCompletionSource<TConnection> ReceiverConnected,
    CancellationTokenSource TimeoutCts
) : IDisposable
{
    public SemaphoreSlim SenderSendLock { get; } = new(1, 1);
    public SemaphoreSlim ReceiverSendLock { get; } = new(1, 1);

    public void Dispose()
    {
        if (!TimeoutCts.IsCancellationRequested)
        {
            TimeoutCts.Cancel();
        }
        TimeoutCts.Dispose();
    }
}
