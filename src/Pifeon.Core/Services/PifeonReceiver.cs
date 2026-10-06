using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;
using System.Text.Json;

namespace Pifeon.Core.Services;

public class PifeonReceiver : IReceiver
{
    private readonly IPeerMessageChannel _peerChannel;
    private readonly ISignalingService? _legacySignalingService;
    private readonly ISession? _session;
    private TransferManifest? _manifest;
    private bool _disposed;

    private const int ChunkSize = PeerMessageChannel.MaximumChunkSize;

    public event Action<long, long, string>? OnProgressChanged;

    public PifeonReceiver(IPeerMessageChannel peerChannel, ISession? session = null)
    {
        _peerChannel = peerChannel ?? throw new ArgumentNullException(nameof(peerChannel));
        _session = session;
    }

    public PifeonReceiver(ISignalingService signalingService, ITransportChannel dataChannel)
    {
        _legacySignalingService = signalingService ?? throw new ArgumentNullException(nameof(signalingService));
        _peerChannel = new PeerMessageChannel(dataChannel ?? throw new ArgumentNullException(nameof(dataChannel)));
    }

    public async Task<TransferManifest> ConnectAndGetManifestAsync(string code, CancellationToken ct = default)
    {
        if (_legacySignalingService is not null)
        {
            await _legacySignalingService.JoinSessionAsync(code, ct);
        }

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

            string manifestJson = System.Text.Encoding.UTF8.GetString(manifestMessage.Payload.Span);
            TransferManifest? manifest = JsonSerializer.Deserialize(manifestJson, SignalingJsonContext.Default.TransferManifest) ?? throw new InvalidOperationException("Failed to deserialize transfer manifest");
            if (manifest.Items is null || manifest.TotalFiles != manifest.Items.Count
                || manifest.Items.Any(item => item.FileSize < 0)
                || manifest.TotalSizeBytes != manifest.Items.Sum(item => item.FileSize))
            {
                throw new InvalidDataException("Invalid transfer manifest file counts or sizes.");
            }
            _manifest = manifest;
            return manifest;
        }
    }

    public async Task ReceiveToDirectoryAsync(string destinationDirectory, CancellationToken ct = default)
    {
        if (_manifest is null)
        {
            throw new InvalidOperationException("Call ConnectAndGetManifestAsync first to receive the manifest");
        }

        string destinationRoot = Path.GetFullPath(destinationDirectory);
        if (!Directory.Exists(destinationRoot))
        {
            Directory.CreateDirectory(destinationRoot);
        }

        long totalBytesReceived = 0;
        long expectedSequenceNumber = 1;

        // Receive each file from the manifest
        foreach (TransferItemInfo item in _manifest.Items)
        {
            string relativePath = item.RelativePath.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            string filePath = Path.GetFullPath(Path.Combine(destinationRoot, relativePath));
            StringComparison comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (Path.IsPathRooted(relativePath) || relativePath.Contains(':')
                || !filePath.StartsWith(Path.TrimEndingDirectorySeparator(destinationRoot) + Path.DirectorySeparatorChar, comparison))
            {
                throw new InvalidDataException("Manifest file path must stay within the destination directory.");
            }
            string? fileDirectory = Path.GetDirectoryName(filePath);

            if (fileDirectory is not null && !Directory.Exists(fileDirectory))
            {
                Directory.CreateDirectory(fileDirectory);
            }

            // Receive all chunks for this file
            using FileStream fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write);
            {
                long bytesRemainingForFile = item.FileSize;

                while (bytesRemainingForFile > 0)
                {
                    // Receive chunk message
                    PeerNetworkMessage chunkMessage = await _peerChannel.ReceiveMessageAsync(ct);

                    if (chunkMessage.Type != PeerNetworkMessageType.ChunkData)
                    {
                        throw new InvalidOperationException($"Expected ChunkData message type, received {chunkMessage.Type}");
                    }

                    if (chunkMessage.SequenceNumber != expectedSequenceNumber || chunkMessage.Payload.Length == 0
                        || chunkMessage.Payload.Length > bytesRemainingForFile || chunkMessage.Payload.Length > ChunkSize)
                    {
                        throw new InvalidDataException("Invalid file chunk sequence or size.");
                    }

                    // Write chunk to file
                    ReadOnlyMemory<byte> chunkData = chunkMessage.Payload;
                    await fileStream.WriteAsync(chunkData, ct);

                    // Update progress
                    bytesRemainingForFile -= chunkData.Length;
                    totalBytesReceived += chunkData.Length;

                    OnProgressChanged?.Invoke(totalBytesReceived, _manifest.TotalSizeBytes, item.RelativePath);

                    // Send acknowledgment
                    PeerNetworkMessage ack = new PeerNetworkMessage(
                        PeerNetworkMessageType.ChunkAck,
                        chunkMessage.SequenceNumber,
                        ReadOnlyMemory<byte>.Empty
                    );
                    await _peerChannel.SendMessageAsync(ack, ct);
                    expectedSequenceNumber++;
                }
            }
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

            if (_legacySignalingService is not null)
            {
                await _legacySignalingService.DisposeAsync();
            }
        }
    }
}
