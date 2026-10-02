namespace Pifeon.Core.Networking;

public readonly record struct PeerNetworkMessage(
    PeerNetworkMessageType Type,
    long SequenceNumber,
    ReadOnlyMemory<byte> Payload
);
