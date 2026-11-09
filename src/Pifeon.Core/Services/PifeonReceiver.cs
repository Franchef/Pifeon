using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;
using Pifeon.Core.StateMachines;
using System.Text.Json;

namespace Pifeon.Core.Services;

internal sealed class PifeonReceiver : IReceiver
{
    private readonly IPeerMessageChannel _peerChannel;
    private readonly ISession? _session;
    private readonly TransferStateMachine _transferState = new();
    private TransferManifest? _manifest;
    private bool _disposed;

    private const int ChunkSize = PeerMessageChannel.MaximumChunkSize;

    public event Action<long, long, string>? OnProgressChanged;

    public PifeonReceiver(IPeerMessageChannel peerChannel, ISession? session = null)
    {
        _peerChannel = peerChannel ?? throw new ArgumentNullException(nameof(peerChannel));
        _session = session;
    }

    public async Task<TransferManifest> ConnectAndGetManifestAsync(string code, CancellationToken ct = default)
    {
        _transferState.TransitionTo(TransferState.WaitingForManifest);
        try
        {
            while (true)
            {
                PeerNetworkMessage manifestMessage = await _peerChannel.ReceiveMessageAsync(ct);

                if (manifestMessage.Type != PeerNetworkMessageType.Manifest)
                {
                    if (manifestMessage.Type == PeerNetworkMessageType.Abort)
                    {
                        string errorMessage = System.Text.Encoding.UTF8.GetString(manifestMessage.Payload.Span);
                        throw new InvalidOperationException(string.IsNullOrWhiteSpace(errorMessage)
                            ? "Unexpected abort while waiting for transfer manifest."
                            : errorMessage);
                    }

                    throw new InvalidOperationException($"Expected Manifest, received {manifestMessage.Type}.");
                }

                TransferManifest? manifest = JsonSerializer.Deserialize(
                    manifestMessage.Payload.Span,
                    SignalingJsonContext.Default.TransferManifest)
                    ?? throw new InvalidOperationException("Failed to deserialize transfer manifest");
                ValidateManifest(manifest);
                _manifest = manifest;
                _transferState.TransitionTo(TransferState.Ready);
                return manifest;
            }
        }
        catch
        {
            _transferState.Fail();
            throw;
        }
    }

    public async Task ReceiveToDirectoryAsync(string destinationDirectory, CancellationToken ct = default)
    {
        if (_manifest is null)
        {
            throw new InvalidOperationException("Call ConnectAndGetManifestAsync first to receive the manifest");
        }

        _transferState.TransitionTo(TransferState.Transferring);
        try
        {
            string destinationRoot = EnsureDestinationRoot(destinationDirectory);
            var progress = new ReceiveProgress(0, 1);

            foreach (TransferItemInfo item in _manifest.Items)
            {
                progress = await ReceiveFileAsync(destinationRoot, item, progress, ct);
            }

            _transferState.TransitionTo(TransferState.Completed);
        }
        catch
        {
            _transferState.Fail();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            if (_session is not null)
            {
                await _session.DisposeAsync();
            }
            else
            {
                await _peerChannel.DisposeAsync();
            }

            _transferState.Dispose();
        }
    }

    private async Task<ReceiveProgress> ReceiveFileAsync(
        string destinationRoot,
        TransferItemInfo item,
        ReceiveProgress progress,
        CancellationToken ct)
    {
        string filePath = ResolveDestinationPath(destinationRoot, item.RelativePath);
        string? fileDirectory = Path.GetDirectoryName(filePath);
        if (fileDirectory is not null)
        {
            EnsureSafeDirectoryPath(destinationRoot, fileDirectory);
        }

        EnsurePathIsNotLink(filePath);
        await using FileStream fileStream = new(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: ChunkSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        long bytesRemaining = item.FileSize;
        long totalBytesReceived = progress.TotalBytes;
        long expectedSequence = progress.NextSequence;

        while (bytesRemaining > 0)
        {
            PeerNetworkMessage chunk = await _peerChannel.ReceiveMessageAsync(ct);
            ValidateChunk(chunk, expectedSequence, bytesRemaining);
            await fileStream.WriteAsync(chunk.Payload, ct);

            bytesRemaining -= chunk.Payload.Length;
            totalBytesReceived += chunk.Payload.Length;
            OnProgressChanged?.Invoke(totalBytesReceived, _manifest!.TotalSizeBytes, item.RelativePath);

            await _peerChannel.SendMessageAsync(
                new PeerNetworkMessage(
                    PeerNetworkMessageType.ChunkAck,
                    chunk.SequenceNumber,
                    ReadOnlyMemory<byte>.Empty),
                ct);
            expectedSequence++;
        }

        return new ReceiveProgress(totalBytesReceived, expectedSequence);
    }

    private static void ValidateManifest(TransferManifest manifest)
    {
        if (manifest.Items is null
            || manifest.TotalFiles != manifest.Items.Count
            || manifest.Items.Any(item => item.FileSize < 0)
            || manifest.TotalSizeBytes != manifest.Items.Sum(item => item.FileSize))
        {
            throw new InvalidDataException("Invalid transfer manifest file counts or sizes.");
        }
    }

    private static string EnsureDestinationRoot(string destinationDirectory)
    {
        string destinationRoot = Path.GetFullPath(destinationDirectory);
        Directory.CreateDirectory(destinationRoot);
        EnsurePathIsNotLink(destinationRoot);
        return destinationRoot;
    }

    private static void EnsureSafeDirectoryPath(string destinationRoot, string directoryPath)
    {
        EnsurePathIsNotLink(destinationRoot);
        string relativePath = Path.GetRelativePath(destinationRoot, directoryPath);
        if (relativePath == ".")
        {
            return;
        }

        string currentPath = destinationRoot;
        foreach (string component in relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries))
        {
            currentPath = Path.Combine(currentPath, component);
            if (File.Exists(currentPath) && !Directory.Exists(currentPath))
            {
                throw new InvalidDataException("Manifest directory path contains a file.");
            }

            Directory.CreateDirectory(currentPath);
            EnsurePathIsNotLink(currentPath);
        }
    }

    private static void EnsurePathIsNotLink(string path)
    {
        FileSystemInfo entry = Directory.Exists(path)
            ? new DirectoryInfo(path)
            : new FileInfo(path);
        entry.Refresh();
        if (entry.LinkTarget is not null
            || (entry.Exists && (entry.Attributes & FileAttributes.ReparsePoint) != 0))
        {
            throw new InvalidDataException("Manifest destination paths cannot contain symbolic links or reparse points.");
        }
    }

    private static string ResolveDestinationPath(string destinationRoot, string manifestPath)
    {
        string relativePath = manifestPath
            .Replace('\\', Path.DirectorySeparatorChar)
            .Replace('/', Path.DirectorySeparatorChar);
        string filePath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
        StringComparison comparison = OperatingSystem.IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string requiredPrefix = Path.TrimEndingDirectorySeparator(destinationRoot) + Path.DirectorySeparatorChar;

        if (Path.IsPathRooted(relativePath)
            || relativePath.Contains(':')
            || !filePath.StartsWith(requiredPrefix, comparison))
        {
            throw new InvalidDataException("Manifest file path must stay within the destination directory.");
        }

        return filePath;
    }

    private static void ValidateChunk(
        PeerNetworkMessage chunk,
        long expectedSequence,
        long bytesRemaining)
    {
        if (chunk.Type != PeerNetworkMessageType.ChunkData)
        {
            throw new InvalidOperationException($"Expected ChunkData message type, received {chunk.Type}");
        }

        if (chunk.SequenceNumber != expectedSequence
            || chunk.Payload.Length == 0
            || chunk.Payload.Length > bytesRemaining
            || chunk.Payload.Length > ChunkSize)
        {
            throw new InvalidDataException("Invalid file chunk sequence or size.");
        }
    }

    private readonly record struct ReceiveProgress(long TotalBytes, long NextSequence);
}
