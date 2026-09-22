using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace Pifeon.Core.IO;

public static class FileHasher
{
    /// <summary>
    /// Calcola l'hash SHA-256 di un file in modalità streaming asincrono.
    /// </summary>
    public static async Task<string> ComputeHashAsync(string filePath, CancellationToken ct = default)
    {
        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException("File non trovato per il calcolo dell'hash.", filePath);
        }

        using var fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            useAsync: true);

        using var sha256 = SHA256.Create();
        byte[] hashBytes = await sha256.ComputeHashAsync(fileStream, ct);

        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}
