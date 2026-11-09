namespace Pifeon.Core.Networking;

internal readonly record struct ServerNetworkMessage(
    ServerNetworkMessageType Type,
    long SequenceNumber,
    ReadOnlyMemory<byte> Payload
);
