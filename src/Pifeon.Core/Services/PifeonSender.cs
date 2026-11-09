using System;
using System.Collections.Generic;
using System.Text.Json;
using Pifeon.Core.Abstractions;
using Pifeon.Core.IO;
using Pifeon.Core.Networking;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;
using Pifeon.Core.StateMachines;

namespace Pifeon.Core.Services;

internal sealed class PifeonSender : ISender
{
    private readonly IPeerMessageChannel _peerChannel;
    private readonly PifeonSession? _session;
    private readonly TransferStateMachine _transferState = new();
    private bool _disposed;

    private const int ChunkSize = PeerMessageChannel.MaximumChunkSize;

    public string Code { get; private set; }
    public bool IsExpired { get; private set; }

    public event Action? OnReceiverJoined;
    public event Action<long, long, string>? OnProgressChanged;

    public PifeonSender(IPeerMessageChannel peerChannel, string code, PifeonSession? session = null)
    {
        _peerChannel = peerChannel ?? throw new ArgumentNullException(nameof(peerChannel));
        Code = code ?? throw new ArgumentNullException(nameof(code));
        _session = session;
        session?.OnReceiverJoined += () => OnReceiverJoined?.Invoke();
    }

    public Task<string> InitializeSessionAsync(CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(Code))
        {
            throw new InvalidOperationException("Session code is not available for this sender.");
        }

        return Task.FromResult(Code);
    }

    public async Task SendAsync(string sourcePath, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(Code))
        {
            throw new InvalidOperationException("Initialize session first using InitializeSessionAsync().");
        }

        _transferState.TransitionTo(TransferState.Preparing);
        try
        {
            List<TransferItem> items = await FolderScanner.ScanPath(sourcePath, computeHash: false, ct).ToListAsync(ct);
            long totalBytes = items.Sum(i => i.FileSize);

            if (_session is not null)
            {
                _transferState.TransitionTo(TransferState.WaitingForConnection);
                await _session.WaitUntilConnectedAsync(ct);
            }

            _transferState.TransitionTo(TransferState.Transferring);
            await SendManifestAsync(items, totalBytes, ct);
            await SendFilesAsync(items, totalBytes, ct);

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

    private async Task SendManifestAsync(
        List<TransferItem> items,
        long totalBytes,
        CancellationToken ct)
    {
        TransferManifest manifest = new(
            items.Count,
            totalBytes,
            [.. items.Select(item => new TransferItemInfo(item.RelativePath, item.FileSize))]);
        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(
            manifest,
            SignalingJsonContext.Default.TransferManifest);

        await _peerChannel.SendMessageAsync(
            new PeerNetworkMessage(PeerNetworkMessageType.Manifest, 0, payload),
            ct);
    }

    private async Task SendFilesAsync(
        List<TransferItem> items,
        long totalBytes,
        CancellationToken ct)
    {
        byte[] buffer = System.Buffers.ArrayPool<byte>.Shared.Rent(ChunkSize);
        var progress = new SendProgress(0, 1);
        try
        {
            foreach (TransferItem item in items)
            {
                progress = await SendFileAsync(item, buffer, progress, totalBytes, ct);
            }
        }
        finally
        {
            System.Buffers.ArrayPool<byte>.Shared.Return(buffer, clearArray: true);
        }
    }

    private async Task<SendProgress> SendFileAsync(
        TransferItem item,
        byte[] buffer,
        SendProgress progress,
        long totalBytes,
        CancellationToken ct)
    {
        await using FileStream fileStream = new(
            item.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: ChunkSize,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (fileStream.Length != item.FileSize)
        {
            throw new IOException($"File size changed after scanning: {item.RelativePath}.");
        }

        long totalBytesSent = progress.TotalBytes;
        long sequenceNumber = progress.NextSequence;
        int bytesRead;
        while ((bytesRead = await fileStream.ReadAsync(buffer.AsMemory(0, ChunkSize), ct)) > 0)
        {
            await _peerChannel.SendMessageAsync(
                new PeerNetworkMessage(
                    PeerNetworkMessageType.ChunkData,
                    sequenceNumber,
                    buffer.AsMemory(0, bytesRead)),
                ct);

            PeerNetworkMessage acknowledgment = await _peerChannel.ReceiveMessageAsync(ct);
            ValidateAcknowledgment(acknowledgment, sequenceNumber);

            totalBytesSent += bytesRead;
            OnProgressChanged?.Invoke(totalBytesSent, totalBytes, item.RelativePath);
            sequenceNumber++;
        }

        return new SendProgress(totalBytesSent, sequenceNumber);
    }

    private static void ValidateAcknowledgment(
        PeerNetworkMessage acknowledgment,
        long expectedSequence)
    {
        if (acknowledgment.Type != PeerNetworkMessageType.ChunkAck)
        {
            throw new InvalidOperationException($"Expected ChunkAck, received {acknowledgment.Type}");
        }

        if (acknowledgment.SequenceNumber != expectedSequence)
        {
            throw new InvalidDataException(
                $"Expected acknowledgment for chunk {expectedSequence}, received {acknowledgment.SequenceNumber}.");
        }
    }

    private readonly record struct SendProgress(long TotalBytes, long NextSequence);
}
