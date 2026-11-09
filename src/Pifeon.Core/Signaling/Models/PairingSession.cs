using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Pifeon.Core.Signaling.Models;

internal sealed record PairingSession<TConnection>(
    string Code,
    TConnection Sender,
    TaskCompletionSource<TConnection> ReceiverConnected,
    CancellationTokenSource TimeoutCts
) : IDisposable
{
    private int _receiverClaimed;

    public SemaphoreSlim SenderSendLock { get; } = new(1, 1);
    public SemaphoreSlim ReceiverSendLock { get; } = new(1, 1);

    public bool TryClaimReceiver() => Interlocked.CompareExchange(ref _receiverClaimed, 1, 0) == 0;

    public void Dispose()
    {
        if (!TimeoutCts.IsCancellationRequested)
        {
            TimeoutCts.Cancel();
        }
        TimeoutCts.Dispose();
    }
}
