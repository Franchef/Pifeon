namespace Pifeon.Core.IO;

public class FileChunkWriter : IDisposable
{
    private readonly FileStream _fileStream;

    public FileChunkWriter(string destinationPath)
    {
        var directory = Path.GetDirectoryName(destinationPath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _fileStream = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            useAsync: true);
    }

    /// <summary>
    /// Scrive un blocco di byte sul file in destinazione.
    /// </summary>
    public async Task WriteChunkAsync(ReadOnlyMemory<byte> chunk, CancellationToken ct = default)
    {
        await _fileStream.WriteAsync(chunk, ct);
    }

    public async Task FlushAsync(CancellationToken ct = default)
    {
        await _fileStream.FlushAsync(ct);
    }

    public void Dispose()
    {
        _fileStream.Dispose();
    }
}
