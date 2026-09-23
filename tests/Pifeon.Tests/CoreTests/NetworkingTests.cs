using System.Buffers.Binary;
using System.Net;
using Pifeon.Core.Networking;

namespace Pifeon.Tests.CoreTests;

public class NetworkingTests
{
    [Fact]
    public void TransportPackage_SerializeAndDeserialize_ShouldPreserveTypeAndPayload()
    {
        // Arrange
        byte[] payloadData = "Handshake-PublicKey-Bytes"u8.ToArray();
        var originalPackage = new TransportPackage
        {
            Type = PackageType.HandshakeKey,
            Payload = payloadData
        };

        // Act
        byte[] serialized = originalPackage.Serialize();

        // Manual Parsing (Simulazione della lettura da socket)
        PackageType packageType = (PackageType)serialized[0];
        int payloadLength = BinaryPrimitives.ReadInt32BigEndian(serialized.AsSpan(1, 4));
        byte[] extractedPayload = serialized.AsSpan(5, payloadLength).ToArray();

        // Assert
        Assert.Equal(PackageType.HandshakeKey, packageType);
        Assert.Equal(payloadData.Length, payloadLength);
        Assert.Equal(payloadData, extractedPayload);
    }

    [Fact]
    public async Task StunClient_WithInvalidStunServer_ShouldReturnNullGracefully()
    {
        // Arrange: Utilizziamo un host inesistente per verificare che il client STUN gestisca i timeout senza eccezioni unhandled
        string invalidStunHost = "nonexistent.stun.server.invalid";

        // Act
        IPEndPoint? endPoint = await StunClient.GetPublicIPEndPointAsync(
            stunServerHost: invalidStunHost,
            stunPort: 19302);

        // Assert
        Assert.Null(endPoint);
    }
}
