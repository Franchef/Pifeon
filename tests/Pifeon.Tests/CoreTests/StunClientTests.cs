using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using Pifeon.Core.Networking;
using Xunit;

namespace Pifeon.Tests.CoreTests;

public class StunClientTests
{
    [Fact]
    public async Task GetPublicIPEndPointAsync_WithValidStunServer_ReturnsCorrectMappedIPAndPort()
    {
        // Arrange: Avviamo un server UDP locale per simulare un server STUN valido
        using var mockStunServer = new UdpClient(0); // 0 = porta libera casuale
        int mockPort = ((IPEndPoint)mockStunServer.Client.LocalEndPoint!).Port;

        var expectedIp = IPAddress.Parse("192.0.2.1"); // IP di test (RFC 5737)
        int expectedPort = 12345;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // Task in background per gestire la risposta STUN mock
        var serverTask = Task.Run(async () =>
        {
            UdpReceiveResult receiveResult = await mockStunServer.ReceiveAsync(cts.Token);

            // Costruiamo la risposta STUN minima (Header STUN + Attr XOR-MAPPED-ADDRESS)
            byte[] response = BuildMockStunResponse(expectedIp, expectedPort, receiveResult.Buffer);
            await mockStunServer.SendAsync(response, response.Length, receiveResult.RemoteEndPoint);
        }, cts.Token);

        // Act
        IPEndPoint? result = await StunClient.GetPublicIPEndPointAsync(
            stunServerHost: "127.0.0.1",
            stunPort: mockPort,
            ct: cts.Token);

        await serverTask;

        // Assert
        Assert.NotNull(result);
        Assert.Equal(expectedIp, result.Address);
        Assert.Equal(expectedPort, result.Port);
    }

    [Fact]
    public async Task GetPublicIPEndPointAsync_WithInvalidHost_ReturnsNullGracefully()
    {
        // Arrange: Host inesistente
        string invalidHost = "nonexistent.stun.host.invalid";

        // Act
        IPEndPoint? result = await StunClient.GetPublicIPEndPointAsync(
            stunServerHost: invalidHost,
            stunPort: 19302);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPublicIPEndPointAsync_WithMalformedResponse_ReturnsNullGracefully()
    {
        // Arrange: Server UDP che risponde con dati spazzatura/non STUN
        using var mockStunServer = new UdpClient(0);
        int mockPort = ((IPEndPoint)mockStunServer.Client.LocalEndPoint!).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        var serverTask = Task.Run(async () =>
        {
            UdpReceiveResult receiveResult = await mockStunServer.ReceiveAsync(cts.Token);
            // Invia una risposta spazzatura di 10 byte (lunghezza insufficiente)
            byte[] garbageResponse = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];
            await mockStunServer.SendAsync(garbageResponse, garbageResponse.Length, receiveResult.RemoteEndPoint);
        }, cts.Token);

        // Act
        IPEndPoint? result = await StunClient.GetPublicIPEndPointAsync(
            stunServerHost: "127.0.0.1",
            stunPort: mockPort,
            ct: cts.Token);

        await serverTask;

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetPublicIPEndPointAsync_WithTimeoutOrNoResponse_ReturnsNullGracefully()
    {
        // Arrange: Server che riceve ma non risponde mai
        using var mockStunServer = new UdpClient(0);
        int mockPort = ((IPEndPoint)mockStunServer.Client.LocalEndPoint!).Port;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Act
        IPEndPoint? result = await StunClient.GetPublicIPEndPointAsync(
            stunServerHost: "127.0.0.1",
            stunPort: mockPort,
            ct: cts.Token);

        // Assert
        Assert.Null(result);
    }

    /// <summary>
    /// Genera una risposta STUN Binding Response valida con l'attributo XOR-MAPPED-ADDRESS (RFC 5389).
    /// </summary>
    private static byte[] BuildMockStunResponse(IPAddress ip, int port, byte[] request)
    {
        // 20 byte Header STUN + 12 byte Attributo XOR-MAPPED-ADDRESS
        byte[] response = new byte[32];

        // Header: STUN Binding Success Response (0x0101)
        response[0] = 0x01;
        response[1] = 0x01;

        // Length: 12 byte di attributo
        response[2] = 0x00;
        response[3] = 0x0C;

        // Copia Magic Cookie e Transaction ID dalla richiesta
        Array.Copy(request, 4, response, 4, 16);

        // Attributo XOR-MAPPED-ADDRESS (Type: 0x0020)
        response[20] = 0x00;
        response[21] = 0x20;

        // Attribute Length: 8 byte
        response[22] = 0x00;
        response[23] = 0x08;

        // Reserved (0x00) & Family IPv4 (0x01)
        response[24] = 0x00;
        response[25] = 0x01;

        // Port XORed con Magic Cookie (0x2112)
        ushort xorPort = (ushort)(port ^ 0x2112);
        response[26] = (byte)(xorPort >> 8);
        response[27] = (byte)(xorPort & 0xFF);

        // IP XORed con Magic Cookie (0x2112A442)
        byte[] ipBytes = ip.GetAddressBytes();
        response[28] = (byte)(ipBytes[0] ^ 0x21);
        response[29] = (byte)(ipBytes[1] ^ 0x12);
        response[30] = (byte)(ipBytes[2] ^ 0xA4);
        response[31] = (byte)(ipBytes[3] ^ 0x42);

        return response;
    }
}
