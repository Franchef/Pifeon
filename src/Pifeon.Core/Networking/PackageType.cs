namespace Pifeon.Core.Networking;

public enum PackageType : byte
{
    HandshakeKey = 0,
    FileMetadata = 1,
    FileChunkData = 2,
    TransferComplete = 3
}
