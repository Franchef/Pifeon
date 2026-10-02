namespace Pifeon.Core.Networking;

public enum ServerNetworkMessageType : byte
{
    Handshake = 1,
    IpExchanges = 2,
    Close = 3,
    Abort = 5
}
