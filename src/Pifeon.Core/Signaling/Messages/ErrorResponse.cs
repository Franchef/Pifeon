using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
internal sealed record ErrorResponse(string Type, string Message);
