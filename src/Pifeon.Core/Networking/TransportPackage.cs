using System.Buffers.Binary;

namespace Pifeon.Core.Networking;

public class TransportPackage
{
    public PackageType Type { get; set; }
    public byte[] Payload { get; set; } = Array.Empty<byte>();

    public byte[] Serialize()
    {
        byte[] result = new byte[1 + 4 + Payload.Length];
        result[0] = (byte)Type;

        // Scrive la lunghezza del payload nei 4 byte successivi (BigEndian)
        BinaryPrimitives.WriteInt32BigEndian(result.AsSpan(1, 4), Payload.Length);

        Buffer.BlockCopy(Payload, 0, result, 5, Payload.Length);
        return result;
    }
}
