namespace Pifeon.Core.Networking;

internal readonly record struct PeerNetworkMessage(
    PeerNetworkMessageType Type,
    long SequenceNumber,
    ReadOnlyMemory<byte> Payload
);
