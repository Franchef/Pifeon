using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
public record JoinSessionRequest(string Code, string Action = "JOIN");
