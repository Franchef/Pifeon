using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
internal sealed record HealthyStatus(string Status);
