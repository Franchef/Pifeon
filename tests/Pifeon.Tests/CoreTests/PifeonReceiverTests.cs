using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Timers;
using Moq;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;
using Pifeon.Core.Signaling;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.Tests.CoreTests;

public sealed class PifeonReceiverTests : IDisposable
{
    private readonly Mock<ISignalingService> _signalingServiceMock;
    private readonly Mock<ITransportChannel> _dataChannelMock;
    private readonly Mock<IPeerMessageChannel> _peerChannelMock;
    private readonly PifeonReceiver _sut; // System Under Test

    public PifeonReceiverTests()
    {
        _signalingServiceMock = new Mock<ISignalingService>();
        _dataChannelMock = new Mock<ITransportChannel>();
        _peerChannelMock = new Mock<IPeerMessageChannel>();

        _sut = new PifeonReceiver(_peerChannelMock.Object);
    }

    public void Dispose()
    {
        _sut.DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    #region 1. Validation & Constructor Tests

    [Fact]
    public void Constructor_ShouldThrowArgumentNullException_WhenPeerChannelIsNull()
    {
        // Act & Assert
        ArgumentNullException ex = Assert.Throws<ArgumentNullException>(() =>
            new PifeonReceiver(null!));

        Assert.Equal("peerChannel", ex.ParamName);
    }

    #endregion

    #region 2. Method Execution Tests

    [Fact]
    public async Task ConnectAndGetManifestAsync_ShouldReturnManifest()
    {
        // Arrange
        string sessionCode = "123456";
        using var cts = new CancellationTokenSource();

        // Setup the mock to return a manifest message
        TransferManifest expectedManifest = new TransferManifest(1, 100, 
            new List<TransferItemInfo> { new TransferItemInfo("test.txt", 100) });
        string manifestJson = JsonSerializer.Serialize(expectedManifest, SignalingJsonContext.Default.TransferManifest);
        byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,
            0,
            manifestPayload
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(manifestMessage)
            .Verifiable();

        // Act
        TransferManifest manifest = await _sut.ConnectAndGetManifestAsync(sessionCode, cts.Token);

        // Assert
        Assert.NotNull(manifest);
        Assert.Equal(expectedManifest.TotalFiles, manifest.TotalFiles);
        Assert.Equal(expectedManifest.TotalSizeBytes, manifest.TotalSizeBytes);
        _peerChannelMock.Verify(p => p.ReceiveMessageAsync(cts.Token), Times.Once);
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldCompleteSuccessfully()
    {
        // Arrange
        string destinationDir = @"C:\Downloads\Pifeon";
        using var cts = new CancellationTokenSource();

        // Must call ConnectAndGetManifestAsync first
        TransferManifest manifest = new TransferManifest(0, 0, []);
        string manifestJson = JsonSerializer.Serialize(manifest, SignalingJsonContext.Default.TransferManifest);
        byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,
            0,
            manifestPayload
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(manifestMessage);

        await _sut.ConnectAndGetManifestAsync("123456", cts.Token);

        // Act & Assert (verifica che non vengano sollevate eccezioni)
        await _sut.ReceiveToDirectoryAsync(destinationDir, cts.Token);
        Assert.True(true); // If we reach here, the method completed successfully
    }

    #endregion

    #region 3. Error Scenarios & Edge Cases

    [Fact]
    public async Task ConnectAndGetManifestAsync_ShouldThrowInvalidOperationException_WhenReceiverSendsWrongMessageType()
    {
        // Arrange
        string sessionCode = "123456";
        using var cts = new CancellationTokenSource();

        // Send wrong message type instead of Manifest
        PeerNetworkMessage wrongMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.ChunkData,
            0,
            System.Text.Encoding.UTF8.GetBytes("wrong")
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(wrongMessage);

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ConnectAndGetManifestAsync(sessionCode, cts.Token));
        Assert.Contains("Expected Manifest", ex.Message);
    }

    [Fact]
    public async Task ConnectAndGetManifestAsync_ShouldThrowInvalidOperationException_WhenManifestIsCorrupted()
    {
        // Arrange
        string sessionCode = "123456";
        using var cts = new CancellationTokenSource();

        // Manifest with corrupted/invalid JSON
        PeerNetworkMessage corruptedMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,
            0,
            System.Text.Encoding.UTF8.GetBytes("{ invalid json }")
        );

        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .ReturnsAsync(corruptedMessage);

        // Act & Assert
        JsonException ex = await Assert.ThrowsAsync<JsonException>(() => _sut.ConnectAndGetManifestAsync(sessionCode, cts.Token));
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldThrowInvalidOperationException_WhenManifestNotReceived()
    {
        // Arrange
        string destinationDir = @"C:\temp";
        using var cts = new CancellationTokenSource();

        // Act & Assert: should throw because ConnectAndGetManifestAsync was not called
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ReceiveToDirectoryAsync(destinationDir, cts.Token));
        Assert.Contains("ConnectAndGetManifestAsync", ex.Message);
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldThrowInvalidOperationException_WhenChunkHasWrongMessageType()
    {
        // Arrange
        string destinationDir = @"C:\temp";
        using var cts = new CancellationTokenSource();

        // Setup manifest
        TransferManifest manifest = new TransferManifest(1, 10, 
            new List<TransferItemInfo> { new TransferItemInfo("test.txt", 10) });
        string manifestJson = JsonSerializer.Serialize(manifest, SignalingJsonContext.Default.TransferManifest);
        byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

        PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
            PeerNetworkMessageType.Manifest,
            0,
            manifestPayload
        );

        // First call returns manifest, subsequent calls return wrong message type
        int callCount = 0;
        _peerChannelMock
            .Setup(p => p.ReceiveMessageAsync(cts.Token))
            .Returns(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    return new ValueTask<PeerNetworkMessage>(manifestMessage);
                }
                // Return wrong type for chunk
                return new ValueTask<PeerNetworkMessage>(new PeerNetworkMessage(
                    PeerNetworkMessageType.Manifest,  // Wrong type
                    1,
                    System.Text.Encoding.UTF8.GetBytes("test")
                ));
            });

        _peerChannelMock
            .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
            .Returns(ValueTask.CompletedTask);

        await _sut.ConnectAndGetManifestAsync("code", cts.Token);

        // Act & Assert
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _sut.ReceiveToDirectoryAsync(destinationDir, cts.Token));
        Assert.Contains("Expected ChunkData", ex.Message);
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldReportProgressChanges()
    {
        // Arrange
        string destinationDir = @"C:\temp";
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PifeonReceiverTest_" + Guid.NewGuid());
        System.IO.Directory.CreateDirectory(tempDir);

        try
        {
            using var cts = new CancellationTokenSource();

            // Setup manifest with one file
            TransferManifest manifest = new TransferManifest(1, 10, 
                new List<TransferItemInfo> { new TransferItemInfo("test.txt", 10) });
            string manifestJson = JsonSerializer.Serialize(manifest, SignalingJsonContext.Default.TransferManifest);
            byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

            PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
                PeerNetworkMessageType.Manifest,
                0,
                manifestPayload
            );

            // Create chunk message
            PeerNetworkMessage chunkMessage = new PeerNetworkMessage(
                PeerNetworkMessageType.ChunkData,
                1,
                System.Text.Encoding.UTF8.GetBytes("0123456789")
            );

            int callCount = 0;
            _peerChannelMock
                .Setup(p => p.ReceiveMessageAsync(cts.Token))
                .Returns(() =>
                {
                    callCount++;
                    return callCount == 1 
                        ? new ValueTask<PeerNetworkMessage>(manifestMessage) 
                        : new ValueTask<PeerNetworkMessage>(chunkMessage);
                });

            _peerChannelMock
                .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
                .Returns(ValueTask.CompletedTask);

            var progressEvents = new List<(long current, long total, string fileName)>();
            _sut.OnProgressChanged += (current, total, fileName) =>
            {
                progressEvents.Add((current, total, fileName));
            };

            await _sut.ConnectAndGetManifestAsync("code", cts.Token);

            // Act
            await _sut.ReceiveToDirectoryAsync(tempDir, cts.Token);

            // Assert: Progress events should have been raised
            Assert.NotEmpty(progressEvents);
            Assert.All(progressEvents, e => Assert.Equal(10, e.total)); // Total bytes
            Assert.Contains(progressEvents, e => e.current == 10); // Final progress
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldCreateNestedDirectories()
    {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PifeonReceiverTest_" + Guid.NewGuid());
        string destinationDir = System.IO.Path.Combine(tempDir, "nested", "path");

        try
        {
            using var cts = new CancellationTokenSource();

            // Setup manifest with nested path
            TransferManifest manifest = new TransferManifest(1, 5, 
                new List<TransferItemInfo> { new TransferItemInfo("nested/subdir/file.txt", 5) });
            string manifestJson = JsonSerializer.Serialize(manifest, SignalingJsonContext.Default.TransferManifest);
            byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

            PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
                PeerNetworkMessageType.Manifest,
                0,
                manifestPayload
            );

            PeerNetworkMessage chunkMessage = new PeerNetworkMessage(
                PeerNetworkMessageType.ChunkData,
                1,
                System.Text.Encoding.UTF8.GetBytes("hello")
            );

            int callCount = 0;
            _peerChannelMock
                .Setup(p => p.ReceiveMessageAsync(cts.Token))
                .Returns(() =>
                {
                    callCount++;
                    return callCount == 1 
                        ? new ValueTask<PeerNetworkMessage>(manifestMessage) 
                        : new ValueTask<PeerNetworkMessage>(chunkMessage);
                });

            _peerChannelMock
                .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
                .Returns(ValueTask.CompletedTask);

            await _sut.ConnectAndGetManifestAsync("code", cts.Token);

            // Act
            await _sut.ReceiveToDirectoryAsync(destinationDir, cts.Token);

            // Assert: File should exist with correct content
            string filePath = System.IO.Path.Combine(destinationDir, "nested", "subdir", "file.txt");
            Assert.True(System.IO.File.Exists(filePath), $"File should exist at {filePath}");
            byte[] content = await System.IO.File.ReadAllBytesAsync(filePath, cts.Token);
            Assert.Equal("hello", System.Text.Encoding.UTF8.GetString(content));
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task ReceiveToDirectoryAsync_ShouldSendChunkAcknowledgments()
    {
        // Arrange
        string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PifeonReceiverTest_" + Guid.NewGuid());
        System.IO.Directory.CreateDirectory(tempDir);

        try
        {
            using var cts = new CancellationTokenSource();

            // Setup manifest
            TransferManifest manifest = new TransferManifest(1, 5, 
                new List<TransferItemInfo> { new TransferItemInfo("test.txt", 5) });
            string manifestJson = JsonSerializer.Serialize(manifest, SignalingJsonContext.Default.TransferManifest);
            byte[] manifestPayload = System.Text.Encoding.UTF8.GetBytes(manifestJson);

            PeerNetworkMessage manifestMessage = new PeerNetworkMessage(
                PeerNetworkMessageType.Manifest,
                0,
                manifestPayload
            );

            PeerNetworkMessage chunkMessage = new PeerNetworkMessage(
                PeerNetworkMessageType.ChunkData,
                1,
                System.Text.Encoding.UTF8.GetBytes("hello")
            );

            int callCount = 0;
            _peerChannelMock
                .Setup(p => p.ReceiveMessageAsync(cts.Token))
                .Returns(() =>
                {
                    callCount++;
                    return callCount == 1 
                        ? new ValueTask<PeerNetworkMessage>(manifestMessage) 
                        : new ValueTask<PeerNetworkMessage>(chunkMessage);
                });

            var sentAcks = new List<PeerNetworkMessage>();
            _peerChannelMock
                .Setup(p => p.SendMessageAsync(It.IsAny<PeerNetworkMessage>(), cts.Token))
                .Callback((PeerNetworkMessage msg, CancellationToken _) =>
                {
                    if (msg.Type == PeerNetworkMessageType.ChunkAck)
                    {
                        sentAcks.Add(msg);
                    }
                })
                .Returns(ValueTask.CompletedTask);

            await _sut.ConnectAndGetManifestAsync("code", cts.Token);

            // Act
            await _sut.ReceiveToDirectoryAsync(tempDir, cts.Token);

            // Assert: Should have sent ChunkAck
            Assert.NotEmpty(sentAcks);
            Assert.All(sentAcks, ack => Assert.Equal(PeerNetworkMessageType.ChunkAck, ack.Type));
        }
        finally
        {
            if (System.IO.Directory.Exists(tempDir))
            {
                System.IO.Directory.Delete(tempDir, recursive: true);
            }
        }
    }

    #endregion

    #region 4. Lifecycle & Disposal Tests

    [Fact]
    public async Task DisposeAsync_ShouldDisposePeerChannel()
    {
        // Arrange
        _peerChannelMock
            .Setup(p => p.DisposeAsync())
            .Returns(ValueTask.CompletedTask)
            .Verifiable();

        // Act
        await _sut.DisposeAsync();

        // Assert
        _peerChannelMock.Verify(p => p.DisposeAsync(), Times.Once);
    }

    [Fact]
    public async Task DisposeAsync_ShouldBeIdempotent_WhenCalledMultipleTimes()
    {
        // Arrange
        _peerChannelMock.Setup(p => p.DisposeAsync()).Returns(ValueTask.CompletedTask);

        // Act
        await _sut.DisposeAsync();
        await _sut.DisposeAsync(); // Second call

        // Assert: DisposeAsync should be called only once
        _peerChannelMock.Verify(p => p.DisposeAsync(), Times.Once);
    }

    #endregion
}
