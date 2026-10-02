namespace Pifeon.Core.Networking;

public readonly record struct ServerNetworkMessage(
    ServerNetworkMessageType Type,
    long SequenceNumber,
    ReadOnlyMemory<byte> Payload
);
