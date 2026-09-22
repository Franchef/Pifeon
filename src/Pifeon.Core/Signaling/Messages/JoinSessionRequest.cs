namespace Pifeon.Core.Signaling.Messages;

public record JoinSessionRequest(string Code, string Action = "JOIN");
