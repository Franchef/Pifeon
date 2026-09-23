using Pifeon.Core.IO;

namespace Pifeon.Tests.CoreTests;

public sealed class FileIoTests : IDisposable
{
    private readonly string _tempSourceFile;
    private readonly string _tempDestinationFile;

    public FileIoTests()
    {
        _tempSourceFile = Path.GetTempFileName();
        _tempDestinationFile = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid()}.tmp");
    }

    [Fact]
    public async Task ComputeHashAsync_ShouldReturnCorrectSha256()
    {
        // Arrange: Scrive dati noti sul file temporaneo
        byte[] content = "Pifeon File Transfer Content"u8.ToArray();
        await File.WriteAllBytesAsync(_tempSourceFile, content);

        // Act
        string hash = await FileHasher.ComputeHashAsync(_tempSourceFile);

        // Assert: Verifica l'hash esadecimale a 64 caratteri
        Assert.Equal(64, hash.Length);
        Assert.False(string.IsNullOrWhiteSpace(hash));
    }

    [Fact]
    public async Task FileChunkReaderAndWriter_ShouldCopyFileChunkByChunkIntegrally()
    {
        // Arrange: Genera un file di test da ~2.5 MB (superiore alla dimensione standard dei chunk)
        byte[] sourceData = new byte[1024 * 1024 * 2 + 512];
        Random.Shared.NextBytes(sourceData);
        await File.WriteAllBytesAsync(_tempSourceFile, sourceData);

        // Act: Legge a chunk e riscrive sul file di destinazione
        using (var reader = new FileChunkReader(_tempSourceFile))
        using (var writer = new FileChunkWriter(_tempDestinationFile))
        {
            byte[] buffer = new byte[1024 * 512]; // Chunk da 512 KB
            int bytesRead;

            while ((bytesRead = await reader.ReadNextChunkAsync(buffer)) > 0)
            {
                await writer.WriteChunkAsync(buffer.AsMemory(0, bytesRead));
            }

            await writer.FlushAsync();
        }

        // Assert: Confronta l'hash del file sorgente e quello ricostruito
        string sourceHash = await FileHasher.ComputeHashAsync(_tempSourceFile);
        string destHash = await FileHasher.ComputeHashAsync(_tempDestinationFile);

        Assert.Equal(sourceHash, destHash);
    }

    public void Dispose()
    {
        if (File.Exists(_tempSourceFile))
            File.Delete(_tempSourceFile);
        if (File.Exists(_tempDestinationFile))
            File.Delete(_tempDestinationFile);
    }
}
