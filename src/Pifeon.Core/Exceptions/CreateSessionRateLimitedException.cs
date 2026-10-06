namespace Pifeon.Core.Exceptions;

public sealed class CreateSessionRateLimitedException : Exception
{
    public CreateSessionRateLimitedException(string? message = null)
        : base(message ?? "Session creation has been rate limited by the signaling server.")
    {
    }
}
