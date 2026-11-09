using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
internal sealed record CodeCreatedResponse(string Type, string Code);
