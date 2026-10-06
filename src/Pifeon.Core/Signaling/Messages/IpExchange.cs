using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
public record IpExchange
{
    public string Type { get; init; } = "IP_EXCHANGE";
    public bool IsSender { get; init; }
    public List<string> LocalIps { get; init; } = [];
    public int Port { get; init; }
    public byte[] PublicKey { get; init; } = [];
}
