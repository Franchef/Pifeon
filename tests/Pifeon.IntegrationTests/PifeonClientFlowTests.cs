using System.Text;
using System.Net.WebSockets;
using Microsoft.AspNetCore.TestHost;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Networking;
using Pifeon.Core.Services;

namespace Pifeon.IntegrationTests;

/// <summary>
/// Integration tests for P2P client flows through ISender/IReceiver interfaces.
/// Tests exercise real WebSocket connections and file transfer scenarios using ServerFixture.
/// These tests verify the actual client-facing APIs that P2P applications use.
/// </summary>
public class PifeonClientFlowTests : IClassFixture<ServerFixture>, IAsyncLifetime
{
    private readonly ServerFixture _factory;
    private const int DefaultTimeoutMs = 15000;
    private string? _tempDirectory;

    public PifeonClientFlowTests(ServerFixture factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync()
    {
        // Create a temporary directory for test files
        _tempDirectory = Path.Combine(Path.GetTempPath(), $"pifeon_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDirectory);
    }

    public async ValueTask DisposeAsync()
    {
        // Clean up temp directory
        if (_tempDirectory != null && Directory.Exists(_tempDirectory))
        {
            try
            {
                Directory.Delete(_tempDirectory, recursive: true);
            }
            catch
            {
                // Best effort cleanup
            }
        }
    }

    private static CancellationTokenSource CreateTimeoutCancellationTokenSource(int timeoutMs = DefaultTimeoutMs)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeoutMs);
        return cts;
    }

    /// <summary>
    /// Tests sender creation and session code generation through ISender interface.
    /// </summary>
    [Fact]
    public async Task Sender_CreateSessionAndInitialize_ShouldGenerateSessionCode()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();

        // Arrange: Create WebSocket client for sender
        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        WebSocket senderSocket = await wsClient.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);

        // Create transport and channels
        var transportChannel = new WebSocketTransportChannel(senderSocket);
        var serverChannel = new ServerMessageChannel(transportChannel);
        var peerChannel = new PeerMessageChannel(transportChannel);

        // Act: Create sender session (manages socket lifetime)
        PifeonSession session = await PifeonSession.CreateSenderAsync(serverChannel, peerChannel, cts.Token);
        await using (session)
        {
            ISender sender = await session.GetSenderAsync(cts.Token);
            string code = await sender.InitializeSessionAsync(cts.Token);

            // Assert: Code should be generated
            Assert.NotEmpty(code);
            Assert.Matches(@"^[A-Z0-9]{6}$", code);
        }
    }

    /// <summary>
    /// Tests receiver joining with valid code through IReceiver interface.
    /// </summary>
    [Fact]
    public async Task Receiver_JoinSessionWithValidCode_ShouldSucceed()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();

        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();

        // Arrange: Create sender session to get valid code
        WebSocket senderSocket = await wsClient.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        var senderTransport = new WebSocketTransportChannel(senderSocket);
        var senderServerChannel = new ServerMessageChannel(senderTransport);
        var senderPeerChannel = new PeerMessageChannel(senderTransport);

        PifeonSession senderSession = await PifeonSession.CreateSenderAsync(senderServerChannel, senderPeerChannel, cts.Token);
        await using (senderSession)
        {
            ISender sender = await senderSession.GetSenderAsync(cts.Token);
            string sessionCode = await sender.InitializeSessionAsync(cts.Token);

            // Act: Create receiver and join
            WebSocket receiverSocket = await wsClient.ConnectAsync(
                new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{sessionCode}"), cts.Token);
            var receiverTransport = new WebSocketTransportChannel(receiverSocket);
            var receiverServerChannel = new ServerMessageChannel(receiverTransport);
            var receiverPeerChannel = new PeerMessageChannel(receiverTransport);

            PifeonSession receiverSession = await PifeonSession.CreateReceiverAsync(sessionCode, receiverServerChannel, receiverPeerChannel, cts.Token);
            await using (receiverSession)
            {
                IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);

                // Assert: Receiver should be created successfully
                Assert.NotNull(receiver);
            }
        }
    }

    /// <summary>
    /// Tests that receiver joining with wrong code raises exception with 404 status.
    /// </summary>
    [Fact]
    public async Task Receiver_JoinSessionWithInvalidCode_ShouldThrowInvalidOperationWith404()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();

        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();

        // Act & Assert: Joining with invalid code should throw
        InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await wsClient.ConnectAsync(
                new Uri(_factory.Server.BaseAddress, "/ws/session/join/INVALID"), cts.Token));

        Assert.Contains("404", ex.Message);
    }

    /// <summary>
    /// Tests the full pairing flow where sender notifies receiver joined event.
    /// </summary>
    [Fact]
    public async Task FullPairingFlow_Sender_CreatesSession_Receiver_Joins_NotificationFires()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();

        bool senderReceivedNotification = false;
        bool receiverWasNotified = false;

        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(FullPairingFlow_Sender_CreatesSession_Receiver_Joins_NotificationFires);

        // Arrange: Create sender
        WebSocket senderSocket = await wsClient.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        var senderTransport = new WebSocketTransportChannel(senderSocket);
        var senderServerChannel = new ServerMessageChannel(senderTransport);
        var senderPeerChannel = new PeerMessageChannel(senderTransport);

        PifeonSession senderSession = await PifeonSession.CreateSenderAsync(senderServerChannel, senderPeerChannel, cts.Token);
        await using (senderSession)
        {
            ISender sender = await senderSession.GetSenderAsync(cts.Token);
            sender.OnReceiverJoined += () => { senderReceivedNotification = true; };
            string sessionCode = await sender.InitializeSessionAsync(cts.Token);

            // Act: Receiver joins in parallel task to avoid blocking
            var receiverTask = Task.Run(async () =>
            {
                try
                {
                    WebSocket receiverSocket = await wsClient.ConnectAsync(
                        new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{sessionCode}"), cts.Token);
                    var receiverTransport = new WebSocketTransportChannel(receiverSocket);
                    var receiverServerChannel = new ServerMessageChannel(receiverTransport);
                    var receiverPeerChannel = new PeerMessageChannel(receiverTransport);

                    PifeonSession receiverSession = await PifeonSession.CreateReceiverAsync(sessionCode, receiverServerChannel, receiverPeerChannel, cts.Token);
                    await using (receiverSession)
                    {
                        IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
                        receiverWasNotified = true;
                        // Keep receiver alive briefly to ensure sender processes the join notification
                        await Task.Delay(300, cts.Token);
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException($"Receiver setup failed: {ex.Message}", ex);
                }
            }, cts.Token);

            // Wait for receiver to join
            await receiverTask;

            // Small delay to let notification propagate
            await Task.Delay(300, cts.Token);

            // Assert: Sender should have received on-receiver-joined notification
            Assert.True(senderReceivedNotification, "Sender should be notified when receiver joins");
            Assert.True(receiverWasNotified, "Receiver should join successfully");
        }
    }

    /// <summary>
    /// Tests directory transfer with multiple files to verify folder structure preservation.
    /// </summary>
    [Fact]
    public async Task FileTransfer_SendDirectory_ReceiverShouldReceiveAllFiles()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource(DefaultTimeoutMs * 2);

        // Arrange: Create a test directory structure
        string sourceDirPath = Path.Combine(_tempDirectory!, "source");
        string subDirPath = Path.Combine(sourceDirPath, "subfolder");
        Directory.CreateDirectory(subDirPath);

        await File.WriteAllTextAsync(Path.Combine(sourceDirPath, "file1.txt"), "Content of file 1", cts.Token);
        await File.WriteAllTextAsync(Path.Combine(sourceDirPath, "file2.txt"), "Content of file 2", cts.Token);
        await File.WriteAllTextAsync(Path.Combine(subDirPath, "file3.txt"), "Content of file 3", cts.Token);

        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(FileTransfer_SendDirectory_ReceiverShouldReceiveAllFiles);

        string? receiverDestination = null;

        try
        {
            // Arrange: Set up sender
            WebSocket senderSocket = await wsClient.ConnectAsync(
                new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
            var senderTransport = new WebSocketTransportChannel(senderSocket);
            var senderServerChannel = new ServerMessageChannel(senderTransport);
            var senderPeerChannel = new PeerMessageChannel(senderTransport);

            PifeonSession senderSession = await PifeonSession.CreateSenderAsync(senderServerChannel, senderPeerChannel, cts.Token);
            await using (senderSession)
            {
                ISender sender = await senderSession.GetSenderAsync(cts.Token);
                string sessionCode = await sender.InitializeSessionAsync(cts.Token);

                // Set up receiver
                WebSocket receiverSocket = await wsClient.ConnectAsync(
                    new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{sessionCode}"), cts.Token);
                var receiverTransport = new WebSocketTransportChannel(receiverSocket);
                var receiverServerChannel = new ServerMessageChannel(receiverTransport);
                var receiverPeerChannel = new PeerMessageChannel(receiverTransport);

                PifeonSession receiverSession = await PifeonSession.CreateReceiverAsync(sessionCode, receiverServerChannel, receiverPeerChannel, cts.Token);
                await using (receiverSession)
                {
                    IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
                    receiverDestination = Path.Combine(_tempDirectory!, $"receive_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(receiverDestination);

                    // Act: Receiver must get manifest BEFORE sender starts (they run concurrently but receiver listens first)
                    // Start receiver manifest fetch as a task so it's ready to listen
                    Task<TransferManifest> manifestTask = receiver.ConnectAndGetManifestAsync(sessionCode, cts.Token);

                    // Small delay to ensure receiver is listening before sender sends
                    await Task.Delay(100, cts.Token);

                    // Now sender starts sending (which will send manifest first)
                    Task senderTask = sender.SendAsync(sourceDirPath, cts.Token);

                    // Wait for both to complete
                    await manifestTask; // Manifest received by receiver
                    Task receiverTask = receiver.ReceiveToDirectoryAsync(receiverDestination, cts.Token);

                    await Task.WhenAll(senderTask, receiverTask);

                    // Assert: All files should be transferred with folder structure
                    Assert.True(File.Exists(Path.Combine(receiverDestination, "file1.txt")));
                    Assert.True(File.Exists(Path.Combine(receiverDestination, "file2.txt")));
                    Assert.True(File.Exists(Path.Combine(receiverDestination, "subfolder", "file3.txt")));

                    string? content = await File.ReadAllTextAsync(Path.Combine(receiverDestination, "file1.txt"), cts.Token);
                    Assert.Equal("Content of file 1", content);
                }
            }
        }
        finally
        {
            if (receiverDestination != null && Directory.Exists(receiverDestination))
            {
                Directory.Delete(receiverDestination, recursive: true);
            }
        }
    }

    /// <summary>
    /// Tests progress tracking during file transfer operations.
    /// </summary>
    [Fact]
    public async Task FileTransfer_WithProgressTracking_ShouldReportProgress()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource(DefaultTimeoutMs * 2);

        // Arrange: Create a large file for measurable progress
        string testFilePath = Path.Combine(_tempDirectory!, "large_transfer_file.bin");
        byte[] testData = new byte[512 * 1024]; // 512 KB
        new Random(42).NextBytes(testData);
        await File.WriteAllBytesAsync(testFilePath, testData, cts.Token);

        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();
        wsClient.ConfigureRequest = req => req.Headers["X-Pifeon-Client-Id"] = nameof(FileTransfer_WithProgressTracking_ShouldReportProgress);

        var senderProgress = new List<(long sent, long total, string fileName)>();
        var receiverProgress = new List<(long received, long total, string fileName)>();
        string? receiverDestination = null;

        try
        {
            // Arrange
            WebSocket senderSocket = await wsClient.ConnectAsync(
                new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
            var senderTransport = new WebSocketTransportChannel(senderSocket);
            var senderServerChannel = new ServerMessageChannel(senderTransport);
            var senderPeerChannel = new PeerMessageChannel(senderTransport);

            PifeonSession senderSession = await PifeonSession.CreateSenderAsync(senderServerChannel, senderPeerChannel, cts.Token);
            await using (senderSession)
            {
                ISender sender = await senderSession.GetSenderAsync(cts.Token);
                sender.OnProgressChanged += (sent, total, fileName) =>
                {
                    senderProgress.Add((sent, total, fileName));
                };

                string sessionCode = await sender.InitializeSessionAsync(cts.Token);

                WebSocket receiverSocket = await wsClient.ConnectAsync(
                    new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{sessionCode}"), cts.Token);
                var receiverTransport = new WebSocketTransportChannel(receiverSocket);
                var receiverServerChannel = new ServerMessageChannel(receiverTransport);
                var receiverPeerChannel = new PeerMessageChannel(receiverTransport);

                PifeonSession receiverSession = await PifeonSession.CreateReceiverAsync(sessionCode, receiverServerChannel, receiverPeerChannel, cts.Token);
                await using (receiverSession)
                {
                    IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
                    receiver.OnProgressChanged += (received, total, fileName) =>
                    {
                        receiverProgress.Add((received, total, fileName));
                    };

                    receiverDestination = Path.Combine(_tempDirectory!, $"receive_{Guid.NewGuid():N}");
                    Directory.CreateDirectory(receiverDestination);

                    // Act: Transfer with progress tracking
                    // Receiver must get manifest BEFORE sender starts  
                    Task<TransferManifest> manifestTask = receiver.ConnectAndGetManifestAsync(sessionCode, cts.Token);

                    // Small delay to ensure receiver is listening
                    await Task.Delay(100, cts.Token);

                    // Sender starts (will send manifest first)
                    Task senderTask = sender.SendAsync(testFilePath, cts.Token);

                    // Wait for manifest and then receive files
                    await manifestTask;
                    Task receiverTask = receiver.ReceiveToDirectoryAsync(receiverDestination, cts.Token);

                    await Task.WhenAll(senderTask, receiverTask);

                    // Assert: Both sides should report progress
                    Assert.NotEmpty(senderProgress);
                    Assert.NotEmpty(receiverProgress);

                    // Verify final progress indicates completion
                    (long sent, long total, string fileName) finalSenderProgress = senderProgress.LastOrDefault();
                    Assert.True(finalSenderProgress.sent >= testData.Length, "Sender should report full transfer");
                }
            }
        }
        finally
        {
            if (receiverDestination != null && Directory.Exists(receiverDestination))
            {
                Directory.Delete(receiverDestination, recursive: true);
            }
        }
    }
}

/// <summary>
/// Integration tests for session expiration scenarios using ShortSessionTimeoutServerFixture.
/// </summary>
public class PifeonSessionExpirationTests : IClassFixture<ShortSessionTimeoutServerFixture>, IAsyncLifetime
{
    private readonly ShortSessionTimeoutServerFixture _factory;
    private const int DefaultTimeoutMs = 15000;

    public PifeonSessionExpirationTests(ShortSessionTimeoutServerFixture factory)
    {
        _factory = factory;
    }

    public async ValueTask InitializeAsync() => await Task.CompletedTask;
    public async ValueTask DisposeAsync() => await Task.CompletedTask;

    private static CancellationTokenSource CreateTimeoutCancellationTokenSource(int timeoutMs = DefaultTimeoutMs)
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeoutMs);
        return cts;
    }

    /// <summary>
    /// Tests that expired sessions cannot be joined.
    /// The ShortSessionTimeoutServerFixture uses a 350ms timeout.
    /// </summary>
    [Fact]
    public async Task JoinSession_AfterExpiration_ShouldFailWith404()
    {
        using CancellationTokenSource cts = CreateTimeoutCancellationTokenSource();

        WebSocketClient wsClient = _factory.Server.CreateWebSocketClient();

        // Arrange: Create a sender session code
        WebSocket senderSocket = await wsClient.ConnectAsync(
            new Uri(_factory.Server.BaseAddress, "/ws/session/create"), cts.Token);
        var senderTransport = new WebSocketTransportChannel(senderSocket);
        var senderServerChannel = new ServerMessageChannel(senderTransport);
        var senderPeerChannel = new PeerMessageChannel(senderTransport);

        PifeonSession senderSession = await PifeonSession.CreateSenderAsync(senderServerChannel, senderPeerChannel, cts.Token);
        await using (senderSession)
        {
            ISender sender = await senderSession.GetSenderAsync(cts.Token);
            string sessionCode = await sender.InitializeSessionAsync(cts.Token);

            // Act: Wait for session to expire (350ms + buffer)
            await Task.Delay(500, cts.Token);

            // Assert: Trying to join should get 404
            InvalidOperationException ex = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await wsClient.ConnectAsync(
                    new Uri(_factory.Server.BaseAddress, $"/ws/session/join/{sessionCode}"), cts.Token));

            Assert.Contains("404", ex.Message);
        }
    }
}
