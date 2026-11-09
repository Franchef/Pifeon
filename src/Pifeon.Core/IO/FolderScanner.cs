using System.Security.Cryptography;
using Pifeon.Core.Abstractions;

namespace Pifeon.Core.IO;

internal static class FolderScanner
{
    public static async IAsyncEnumerable<TransferItem> ScanPath(
        string path,
        bool computeHash = true,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            string hash = computeHash ? await ComputeFileHash(fileInfo.FullName, ct) : string.Empty;
            yield return new TransferItem(fileInfo.Name, fileInfo.Length, fileInfo.FullName, hash);
        }
        else if (Directory.Exists(path))
        {
            var rootDir = new DirectoryInfo(path);
            foreach (FileInfo file in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                string relativePath = Path.GetRelativePath(rootDir.FullName, file.FullName);
                string hash = computeHash ? await ComputeFileHash(file.FullName, ct) : string.Empty;
                yield return new TransferItem(relativePath, file.Length, file.FullName, hash);
            }
        }
        else
        {
            throw new FileNotFoundException($"Il percorso specificato non esiste: {path}");
        }
    }

    /// <summary>
    /// Calcola l'hash SHA-256 del file in streaming, senza caricare l'intero file in memoria RAM.
    /// </summary>
    private static async Task<string> ComputeFileHash(string filePath, CancellationToken ct)
    {
        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hashBytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexStringLower(hashBytes); // Output hex string (es. "a3f5...")
    }
}
