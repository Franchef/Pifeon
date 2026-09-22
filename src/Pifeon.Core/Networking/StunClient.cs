using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Pifeon.Core.Networking;

public static class StunClient
{
    /// <summary>
    /// Risolve l'indirizzo IP e la porta pubblica del client usando un server STUN (RFC 5389 semplice).
    /// </summary>
    public static async Task<IPEndPoint?> GetPublicIPEndPointAsync(string stunServerHost = "stun.l.google.com", int stunPort = 19302, CancellationToken ct = default)
    {
        try
        {
            using var udpClient = new UdpClient();
            udpClient.Client.SendTimeout = 3000;
            udpClient.Client.ReceiveTimeout = 3000;

            // Messaggio STUN Binding Request minimale (20 byte)
            byte[] stunRequest = new byte[20];
            stunRequest[0] = 0x00;
            stunRequest[1] = 0x01; // Binding Request
            stunRequest[2] = 0x00;
            stunRequest[3] = 0x00; // Message Length: 0
            // Magic Cookie (RFC 5389)
            stunRequest[4] = 0x21;
            stunRequest[5] = 0x12;
            stunRequest[6] = 0xA4;
            stunRequest[7] = 0x42;
            // Transaction ID casuale (12 byte)
            Random.Shared.NextBytes(stunRequest.AsSpan(8, 12));

            IPAddress[] serverAddresses = await Dns.GetHostAddressesAsync(stunServerHost, ct);
            var serverEndPoint = new IPEndPoint(serverAddresses[0], stunPort);

            await udpClient.SendAsync(stunRequest, stunRequest.Length, serverEndPoint);

            UdpReceiveResult receiveResult = await udpClient.ReceiveAsync(ct);
            byte[] response = receiveResult.Buffer;

            // Parsing basilare della risposta XOR-MAPPED-ADDRESS
            for (int i = 20; i < response.Length - 4;)
            {
                ushort type = (ushort)((response[i] << 8) | response[i + 1]);
                ushort length = (ushort)((response[i + 2] << 8) | response[i + 3]);

                if (type == 0x0020) // XOR-MAPPED-ADDRESS
                {
                    byte family = response[i + 5];
                    if (family == 0x01) // IPv4
                    {
                        ushort xorPort = (ushort)((response[i + 6] << 8) | response[i + 7]);
                        int port = xorPort ^ 0x2112;

                        byte[] ipBytes =
                        [
                            (byte)(response[i + 8] ^ 0x21),
                            (byte)(response[i + 9] ^ 0x12),
                            (byte)(response[i + 10] ^ 0xA4),
                            (byte)(response[i + 11] ^ 0x42),
                        ];

                        return new IPEndPoint(new IPAddress(ipBytes), port);
                    }
                }
                i += 4 + length;
            }
        }
        catch
        {
            // Fallback se il server STUN non risponde o il firewall blocca UDP
        }

        return null;
    }
}
