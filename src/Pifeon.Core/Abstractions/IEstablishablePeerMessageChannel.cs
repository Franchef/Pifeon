using Pifeon.Core.Networking;

namespace Pifeon.Core.Abstractions;

internal interface IEstablishablePeerMessageChannel : IPeerMessageChannel
{
    Task EstablishAsync(
        IServerMessageChannel signaling,
        string code,
        PeerRole role,
        CancellationToken ct = default);
}
