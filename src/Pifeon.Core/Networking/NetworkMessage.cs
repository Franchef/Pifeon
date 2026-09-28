namespace Pifeon.Core.Networking;

public readonly record struct NetworkMessage(
    MessageType Type,
    long SequenceNumber,
    ReadOnlyMemory<byte> Payload
);
