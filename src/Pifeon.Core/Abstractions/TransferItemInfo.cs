using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Abstractions;

[ExcludeFromCodeCoverage]
public record TransferItemInfo(string RelativePath, long FileSize);
