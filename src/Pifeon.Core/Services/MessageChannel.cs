using System;
using System.Collections.Generic;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;

namespace Pifeon.Core.Services;

public sealed class MessageChannel(ITransportChannel transportChannel) : IMessageChannel
{
    public bool IsConnected => transportChannel.IsConnected;
    public Task CloseAsync(CancellationToken ct = default) => transportChannel.CloseAsync(ct);
    public ValueTask DisposeAsync() => transportChannel.DisposeAsync();
    public ValueTask<NetworkMessage> ReceiveMessageAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }

    public ValueTask SendMessageAsync(NetworkMessage message, CancellationToken ct = default)
    {
        throw new NotImplementedException();
    }
}
