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
    private TransferManifest? _manifest;
    private bool _disposed;

    private const int ChunkSize = 64 * 1024; // 64 KB chunks

    public event Action<long, long, string>? OnProgressChanged;

    public PifeonReceiver(IPeerMessageChannel peerChannel)
    {
        _peerChannel = peerChannel ?? throw new ArgumentNullException(nameof(peerChannel));
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

        // Receive the manifest message from the sender via peer channel
        PeerNetworkMessage manifestMessage = await _peerChannel.ReceiveMessageAsync(ct);

        if (manifestMessage.Type != PeerNetworkMessageType.Manifest)
        {
            throw new InvalidOperationException($"Expected Manifest message type, received {manifestMessage.Type}");
        }

        // Deserialize manifest from payload (JSON encoded)
        string manifestJson = System.Text.Encoding.UTF8.GetString(manifestMessage.Payload.Span);
        TransferManifest? manifest = JsonSerializer.Deserialize(manifestJson, SignalingJsonContext.Default.TransferManifest);

        if (manifest is null)
        {
            throw new InvalidOperationException("Failed to deserialize transfer manifest");
        }

        _manifest = manifest;
        return manifest;
    }

    public async Task ReceiveToDirectoryAsync(string destinationDirectory, CancellationToken ct = default)
    {
        if (_manifest is null)
        {
            throw new InvalidOperationException("Call ConnectAndGetManifestAsync first to receive the manifest");
        }

        if (!Directory.Exists(destinationDirectory))
        {
            Directory.CreateDirectory(destinationDirectory);
        }

        long totalBytesReceived = 0;

        // Receive each file from the manifest
        foreach (TransferItemInfo item in _manifest.Items)
        {
            string filePath = Path.Combine(destinationDirectory, item.RelativePath);
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

                    // Write chunk to file
                    byte[] chunkData = chunkMessage.Payload.ToArray();
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
                }
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (!_disposed)
        {
            _disposed = true;
            await _peerChannel.DisposeAsync();

            if (_legacySignalingService is not null)
            {
                await _legacySignalingService.DisposeAsync();
            }
        }
    }
}
