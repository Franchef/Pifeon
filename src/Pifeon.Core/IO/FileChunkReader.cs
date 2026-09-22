namespace Pifeon.Core.IO;

public class FileChunkReader : IDisposable
{
    private readonly FileStream _fileStream;
    public const int DefaultChunkSize = 1024 * 1024; // 1 MB per chunk

    public long TotalBytes => _fileStream.Length;

    public FileChunkReader(string filePath)
    {
        _fileStream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: DefaultChunkSize,
            useAsync: true);
    }

    /// <summary>
    /// Legge il prossimo chunk dal file. Restituisce 0 byte quando il file è terminato.
    /// </summary>
    public async Task<int> ReadNextChunkAsync(Memory<byte> buffer, CancellationToken ct = default)
    {
        return await _fileStream.ReadAsync(buffer, ct);
    }

    public void Dispose()
    {
        _fileStream.Dispose();
    }
}
