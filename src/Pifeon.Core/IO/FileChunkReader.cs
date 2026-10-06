namespace Pifeon.Core.IO;

public sealed class FileChunkReader : IDisposable
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
    /// Reads the next chunk from the file. Returns 0 bytes when the file is finished.
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
