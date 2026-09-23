using System.Buffers.Binary;
using Pifeon.Core.Networking;

namespace Pifeon.Tests.CoreTests;

public class TransportPackageTests
{
    [Fact]
    public void TransportPackage_Serialize_ShouldMatchBinaryHeaderStructure()
    {
        // Arrange
        byte[] payload = "Test Payload"u8.ToArray();
        var package = new TransportPackage
        {
            Type = PackageType.FileChunkData,
            Payload = payload
        };

        // Act
        byte[] serialized = package.Serialize();

        // Assert
        Assert.Equal(1 + 4 + payload.Length, serialized.Length);
        Assert.Equal((byte)PackageType.FileChunkData, serialized[0]);

        // Legge il prefisso di lunghezza dai byte 1-4 (BigEndian)
        int readLength = BinaryPrimitives.ReadInt32BigEndian(serialized.AsSpan(1, 4));
        Assert.Equal(payload.Length, readLength);
    }
}
