namespace Pifeon.Core.Configuration;

public static partial class PifeonConfigurationResolver
{
    public record PifeonServerConfiguration
    {
        public int SessionTimeoutSeconds { get; init; } = 300; // Default session timeout in seconds
    }
}
