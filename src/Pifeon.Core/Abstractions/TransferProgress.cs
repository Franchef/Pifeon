using System.Diagnostics.CodeAnalysis;

namespace Pifeon.Core.Abstractions;

/// <summary>Reports progress for the current file and the overall transfer.</summary>
/// <param name="FileIndex">The one-based index of the current file in the manifest.</param>
/// <param name="TotalFiles">The number of files in the manifest.</param>
/// <param name="RelativePath">The current file's manifest-relative path.</param>
/// <param name="FileBytesTransferred">Bytes transferred for the current file.</param>
/// <param name="FileSizeBytes">The current file's total size in bytes.</param>
/// <param name="TotalBytesTransferred">Cumulative bytes transferred across all files.</param>
/// <param name="TotalSizeBytes">The total size of all files in bytes.</param>
/// <param name="IsFileCompleted">Whether the current file is complete, including empty files.</param>
/// <param name="IsTransferCompleted">Whether all files in the manifest are complete.</param>
[ExcludeFromCodeCoverage]
public sealed record TransferProgress(
    int FileIndex,
    int TotalFiles,
    string RelativePath,
    long FileBytesTransferred,
    long FileSizeBytes,
    long TotalBytesTransferred,
    long TotalSizeBytes,
    bool IsFileCompleted,
    bool IsTransferCompleted);
