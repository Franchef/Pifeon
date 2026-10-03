namespace Pifeon.Core.Exceptions;

public sealed class CreateSessionRateLimitedException : Exception
{
    public CreateSessionRateLimitedException()
        : base("Session creation has been rate limited by the signaling server.")
    {
    }

    public CreateSessionRateLimitedException(string message)
        : base(message)
    {
    }
}
