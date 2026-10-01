using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
public record IpExchange
{
    public List<string> LocalIps { get; init; }
    public int Port { get; init; }
}
