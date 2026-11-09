using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
internal sealed record JoinSessionRequest(string Code, string Action = "JOIN");
