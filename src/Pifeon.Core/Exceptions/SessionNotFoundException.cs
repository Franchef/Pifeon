namespace Pifeon.Core.Exceptions;

public sealed class SessionNotFoundException : Exception
{
    public SessionNotFoundException(string code)
        : base($"Session '{code}' was not found.")
    {
        Code = code;
    }

    public string Code { get; }
}
