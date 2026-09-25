using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Server;

[ExcludeFromCodeCoverage]
public record Todo(int Id, string? Title, DateOnly? DueBy = null, bool IsComplete = false);
