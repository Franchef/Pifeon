using System.Security.Cryptography;
using Pifeon.Core.Abstractions;

namespace Pifeon.Core.IO;

public static class FolderScanner
{
    public static IEnumerable<TransferItem> ScanPath(string path)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            string hash = ComputeFileHash(fileInfo.FullName);
            yield return new TransferItem(fileInfo.Name, fileInfo.Length, fileInfo.FullName, hash);
        }
        else if (Directory.Exists(path))
        {
            var rootDir = new DirectoryInfo(path);
            foreach (FileInfo file in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                string relativePath = Path.GetRelativePath(rootDir.FullName, file.FullName);
                string hash = ComputeFileHash(file.FullName);
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
    private static string ComputeFileHash(string filePath)
    {
        using FileStream stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        byte[] hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexStringLower(hashBytes); // Output hex string (es. "a3f5...")
    }
}
