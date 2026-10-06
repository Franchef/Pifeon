using System;
using System.Collections.Generic;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.IO;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Core.Services;

public class PifeonSender : ISender
{
    private readonly IPeerMessageChannel _peerChannel;
    private readonly ISignalingService? _legacySignalingService;
    private readonly PifeonSession? _session;
    private bool _disposed;

    private const int ChunkSize = PeerMessageChannel.MaximumChunkSize;

    public string Code { get; private set; }
    public bool IsExpired { get; private set; }

    public event Action? OnReceiverJoined;
    public event Action<long, long, string>? OnProgressChanged;

    public PifeonSender(IPeerMessageChannel peerChannel, string code)
    {
        _peerChannel = peerChannel ?? throw new ArgumentNullException(nameof(peerChannel));
        Code = code ?? throw new ArgumentNullException(nameof(code));
    }

    public PifeonSender(IPeerMessageChannel peerChannel, string code, PifeonSession? session = null)
    {
        _peerChannel = peerChannel ?? throw new ArgumentNullException(nameof(peerChannel));
        Code = code ?? throw new ArgumentNullException(nameof(code));
        _session = session;
        session?.OnReceiverJoined += () => OnReceiverJoined?.Invoke();
    }

    public PifeonSender(ISignalingService signalingService, ITransportChannel dataChannel)
    {
        _legacySignalingService = signalingService ?? throw new ArgumentNullException(nameof(signalingService));
        _peerChannel = new PeerMessageChannel(dataChannel ?? throw new ArgumentNullException(nameof(dataChannel)));

        _legacySignalingService.OnReceiverJoined += () => OnReceiverJoined?.Invoke();
    }

    public async Task<string> InitializeSessionAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(Code))
        {
            if (_legacySignalingService is null)
            {
                throw new InvalidOperationException("Session code is not available for this sender.");
            }

            Code = await _legacySignalingService.CreateSessionAsync(ct);
        }

        return Code;
    }

    public async Task SendAsync(string sourcePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Code))
        {
            throw new InvalidOperationException("Initialize session first using InitializeSessionAsync().");
        }

        // 1. Scan the local path (file or folder with SHA-256 calculation)
        List<TransferItem> items = await FolderScanner.ScanPath(sourcePath).ToListAsync(ct);
        long totalBytes = items.Sum(i => i.FileSize);

        // 2. For legacy compatibility wait for signaling; in session mode the connection is ready
        if (_legacySignalingService is not null)
        {
            await _legacySignalingService.WaitForReceiverAsync(ct);
        }
        if (_session is not null)
        {
            await _session.WaitUntilConnectedAsync(ct);
        }

        // 3. Send manifest
        TransferManifest manifest = new TransferManifest(
            items.Count,
            totalBytes,
            [.. items.Select(i => new TransferItemInfo(i.RelativePath, i.FileSize))]
        );

        string manifestJson = JsonSerializer.Serialize(manifest, SignalingJsonContext.Default.TransferManifest);
        byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,
            0,
            manifestPayload
        );

        await _peerChannel.SendMessageAsync(manifestMessage, ct);

        // 4. Stream chunks for each file
        long totalBytesSent = 0;
        long sequenceNumber = 1;

        foreach (TransferItem item in items)
        {
            using FileStream fileStream = new FileStream(item.FullPath, FileMode.Open, FileAccess.Read);
            {
                if (fileStream.Length != item.FileSize)
                {
                    throw new IOException($"File size changed after scanning: {item.RelativePath}.");
                }
                byte[] buffer = new byte[ChunkSize];
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(), ct)) > 0)
                {
                    // Send chunk
                    PeerNetworkMessage chunkMessage = new PeerNetworkMessage(
                        PeerNetworkMessageType.ChunkData,
                        sequenceNumber,
                        buffer.AsMemory(0, bytesRead)
                    );

                    await _peerChannel.SendMessageAsync(chunkMessage, ct);

                    // Wait for acknowledgment
                    PeerNetworkMessage ackMessage = await _peerChannel.ReceiveMessageAsync(ct);
                    if (ackMessage.Type != PeerNetworkMessageType.ChunkAck)
                    {
                        throw new InvalidOperationException($"Expected ChunkAck, received {ackMessage.Type}");
                    }
                    if (ackMessage.SequenceNumber != sequenceNumber)
                    {
                        throw new InvalidDataException($"Expected acknowledgment for chunk {sequenceNumber}, received {ackMessage.SequenceNumber}.");
                    }

                    // Update progress
                    totalBytesSent += bytesRead;
                    OnProgressChanged?.Invoke(totalBytesSent, totalBytes, item.RelativePath);

                    sequenceNumber++;
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
