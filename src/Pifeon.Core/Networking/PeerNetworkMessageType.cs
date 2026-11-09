namespace Pifeon.Core.Networking;

internal enum PeerNetworkMessageType : byte
{
    Handshake = 1,
    Manifest = 2,
    ChunkData = 3,
    ChunkAck = 4,
    Abort = 5
}
