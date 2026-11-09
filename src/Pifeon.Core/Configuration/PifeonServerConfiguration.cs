namespace Pifeon.Core.Configuration;


internal sealed record PifeonServerConfiguration
{
    public int SessionTimeoutSeconds { get; init; } = 300; // Default session timeout in seconds
}
