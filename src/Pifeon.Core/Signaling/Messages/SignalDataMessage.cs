using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Signaling.Messages;

[ExcludeFromCodeCoverage]
internal sealed record SignalDataMessage(string Type, string Payload);
