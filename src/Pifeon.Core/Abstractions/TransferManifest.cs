using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Abstractions;

[ExcludeFromCodeCoverage]
public record TransferManifest(
    int TotalFiles,
    long TotalSizeBytes,
    IReadOnlyList<TransferItemInfo> Items
);
