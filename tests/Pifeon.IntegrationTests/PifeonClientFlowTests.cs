using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Pifeon.Core;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Exceptions;
using Pifeon.Core.Signaling.Messages;

namespace Pifeon.IntegrationTests;

public sealed class PifeonClientFlowTests : IClassFixture<DirectTransferServerFixture>, IAsyncLifetime
{
    private readonly DirectTransferServerFixture _factory;
    private readonly string _tempDirectory = Path.Combine(Path.GetTempPath(), $"pifeon_flow_{Guid.NewGuid():N}");

    public PifeonClientFlowTests(DirectTransferServerFixture factory)
    {
        _factory = factory;
    }

    public ValueTask InitializeAsync()
    {
        Directory.CreateDirectory(_tempDirectory);
        return ValueTask.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        Directory.Delete(_tempDirectory, recursive: true);
        return ValueTask.CompletedTask;
    }

    private static CancellationTokenSource CreateTimeout()
    {
        CancellationTokenSource cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        return cts;
    }

    private PifeonServer CreateClient()
    {
        using HttpClient httpClient = _factory.CreateClient();
        var uri = new UriBuilder(httpClient.BaseAddress!) { Scheme = "ws" };
        return new PifeonServer(uri.Uri.ToString());
    }

    private async Task AssertSignalingSessionRemovedAsync(string code, CancellationToken ct)
    {
        IPairingManager<WebSocket> manager = _factory.Services.GetRequiredService<IPairingManager<WebSocket>>();
        while (manager.TryGetSession(code, out _))
        {
            await Task.Delay(10, ct);
        }
        Assert.False(manager.TryGetSession(code, out _));
    }

    private static async Task<TransferManifest> TransferAsync(
        ISender sender, IReceiver receiver, string code, string source, string destination, CancellationToken ct)
    {
        async Task<TransferManifest> ReceiveAsync()
        {
            TransferManifest manifest = await receiver.ConnectAndGetManifestAsync(code, ct);
            await receiver.ReceiveToDirectoryAsync(destination, ct);
            return manifest;
        }

        Task<TransferManifest> receiving = ReceiveAsync();
        await Task.WhenAll(sender.SendAsync(source, ct), receiving);
        return await receiving;
    }

    [Fact]
    public async Task CreateSession_ShouldReturnHighEntropyCodeBeforePeerConnects()
    {
        using CancellationTokenSource cts = CreateTimeout();
        await using PifeonServer client = CreateClient();
        await using ISenderSession session = await client.CreateSessionHandleAsync(cts.Token);
        ISender sender = await session.GetSenderAsync(cts.Token);

        Assert.Matches("^[2-9A-HJ-NP-Z]{10}$", session.Code);
        Assert.Equal(session.Code, await sender.InitializeSessionAsync(cts.Token));
        Assert.False(session.IsConnected);
    }

    [Fact]
    public async Task JoinSession_ShouldConfirmDirectConnectionAndRaiseNotificationOnce()
    {
        using CancellationTokenSource cts = CreateTimeout();
        await using PifeonServer client = CreateClient();
        await using ISenderSession senderSession = await client.CreateSessionHandleAsync(cts.Token);
        ISender sender = await senderSession.GetSenderAsync(cts.Token);
        var joined = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int notificationCount = 0;
        sender.OnReceiverJoined += () =>
        {
            Interlocked.Increment(ref notificationCount);
            joined.TrySetResult();
        };
        Assert.False(senderSession.IsConnected);

        await using IReceiverSession receiverSession = await client.JoinSessionHandleAsync(senderSession.Code, cts.Token);
        IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
        await senderSession.WaitUntilConnectedAsync(cts.Token);
        await joined.Task.WaitAsync(cts.Token);

        Assert.NotNull(receiver);
        Assert.True(senderSession.IsConnected);
        Assert.True(receiverSession.IsConnected);
        Assert.Equal(1, notificationCount);
        await AssertSignalingSessionRemovedAsync(senderSession.Code, cts.Token);
    }

    [Fact]
    public async Task JoinSession_WithInvalidCode_ShouldReportSessionNotFound()
    {
        using CancellationTokenSource cts = CreateTimeout();
        await using PifeonServer client = CreateClient();
        await Assert.ThrowsAsync<SessionNotFoundException>(() => client.JoinSessionHandleAsync("000000", cts.Token));
    }

    [Fact]
    public async Task CancelConnectionWait_ShouldAllowClosingUnpairedSession()
    {
        using CancellationTokenSource cts = CreateTimeout();
        await using PifeonServer client = CreateClient();
        await using ISenderSession session = await client.CreateSessionHandleAsync(cts.Token);
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => session.WaitUntilConnectedAsync(canceled.Token));
        await session.CloseAsync(cts.Token);
        await session.CloseAsync(cts.Token);

        Assert.False(session.IsConnected);
        await AssertSignalingSessionRemovedAsync(session.Code, cts.Token);
    }

    [Fact]
    public async Task DirectoryTransfer_ShouldPreserveEveryFileAfterSignalingIsRemoved()
    {
        using CancellationTokenSource cts = CreateTimeout();
        string source = Path.Combine(_tempDirectory, "source");
        string destination = Path.Combine(_tempDirectory, "received");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        var expected = new Dictionary<string, byte[]>
        {
            ["first.bin"] = RandomNumberGenerator.GetBytes(65537),
            [Path.Combine("nested", "second.bin")] = RandomNumberGenerator.GetBytes(196613),
            [Path.Combine("nested", "empty.bin")] = []
        };
        foreach (KeyValuePair<string, byte[]> file in expected)
        {
            await File.WriteAllBytesAsync(Path.Combine(source, file.Key), file.Value, cts.Token);
        }
        await using PifeonServer client = CreateClient();
        await using ISenderSession senderSession = await client.CreateSessionHandleAsync(cts.Token);
        ISender sender = await senderSession.GetSenderAsync(cts.Token);
        await using IReceiverSession receiverSession = await client.JoinSessionHandleAsync(senderSession.Code, cts.Token);
        IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
        await senderSession.WaitUntilConnectedAsync(cts.Token);
        await AssertSignalingSessionRemovedAsync(senderSession.Code, cts.Token);

        TransferManifest manifest = await TransferAsync(sender, receiver, senderSession.Code, source, destination, cts.Token);

        Assert.Equal(expected.Count, manifest.TotalFiles);
        Assert.Equal(expected.Values.Sum(bytes => (long)bytes.Length), manifest.TotalSizeBytes);
        Assert.Equal(expected.Count, Directory.GetFiles(destination, "*", SearchOption.AllDirectories).Length);
        foreach (KeyValuePair<string, byte[]> file in expected)
        {
            Assert.Equal(file.Value, await File.ReadAllBytesAsync(Path.Combine(destination, file.Key), cts.Token));
        }
    }

    [Fact]
    public async Task LargeManifest_ShouldTransferEmptyFilesAndBinaryData()
    {
        using CancellationTokenSource cts = CreateTimeout();
        string source = Path.Combine(_tempDirectory, "source");
        string destination = Path.Combine(_tempDirectory, "received");
        Directory.CreateDirectory(source);
        for (int i = 0; i < 160; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(source, $"{new string('a', 100)}-{i}.txt"), [], cts.Token);
        }
        byte[] data = RandomNumberGenerator.GetBytes(196613);
        await File.WriteAllBytesAsync(Path.Combine(source, "data.bin"), data, cts.Token);
        await using PifeonServer client = CreateClient();
        await using ISenderSession senderSession = await client.CreateSessionHandleAsync(cts.Token);
        ISender sender = await senderSession.GetSenderAsync(cts.Token);
        await using IReceiverSession receiverSession = await client.JoinSessionHandleAsync(senderSession.Code, cts.Token);
        IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
        await senderSession.WaitUntilConnectedAsync(cts.Token);
        await AssertSignalingSessionRemovedAsync(senderSession.Code, cts.Token);

        TransferManifest manifest = await TransferAsync(sender, receiver, senderSession.Code, source, destination, cts.Token);

        Assert.True(JsonSerializer.SerializeToUtf8Bytes(manifest, SignalingJsonContext.Default.TransferManifest).Length > 16 * 1024);
        Assert.Equal(161, manifest.TotalFiles);
        Assert.Equal(data.Length, manifest.TotalSizeBytes);
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(destination, "data.bin"), cts.Token));
        Assert.Equal(160, Directory.GetFiles(destination, "*.txt").Length);
        Assert.All(Directory.GetFiles(destination, "*.txt"), path => Assert.Equal(0, new FileInfo(path).Length));
    }

    [Fact]
    public async Task FileTransfer_ShouldReportExactProgressForEveryChunk()
    {
        using CancellationTokenSource cts = CreateTimeout();
        string file = Path.Combine(_tempDirectory, "data.bin");
        string destination = Path.Combine(_tempDirectory, "received");
        byte[] data = RandomNumberGenerator.GetBytes(512 * 1024 + 7);
        await File.WriteAllBytesAsync(file, data, cts.Token);
        await using PifeonServer client = CreateClient();
        await using ISenderSession senderSession = await client.CreateSessionHandleAsync(cts.Token);
        ISender sender = await senderSession.GetSenderAsync(cts.Token);
        await using IReceiverSession receiverSession = await client.JoinSessionHandleAsync(senderSession.Code, cts.Token);
        IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
        var sent = new List<(long Current, long Total, string File)>();
        var received = new List<(long Current, long Total, string File)>();
        sender.OnProgressChanged += (current, total, name) => sent.Add((current, total, name));
        receiver.OnProgressChanged += (current, total, name) => received.Add((current, total, name));

        await TransferAsync(sender, receiver, senderSession.Code, file, destination, cts.Token);

        Assert.Equal(9, sent.Count);
        Assert.Equal(sent, received);
        for (int i = 0; i < sent.Count; i++)
        {
            Assert.Equal(Math.Min((i + 1) * 65536L, data.Length), sent[i].Current);
            Assert.Equal(data.Length, sent[i].Total);
            Assert.Equal("data.bin", sent[i].File);
        }
        Assert.Equal(data, await File.ReadAllBytesAsync(Path.Combine(destination, "data.bin"), cts.Token));
    }

    [Fact]
    public async Task FolderTransfer_ShouldReportPerFileAndOverallProgressOnBothPeers()
    {
        using CancellationTokenSource cts = CreateTimeout();
        string source = Path.Combine(_tempDirectory, "source");
        string destination = Path.Combine(_tempDirectory, "received");
        Directory.CreateDirectory(Path.Combine(source, "nested"));
        var files = new Dictionary<string, byte[]>
        {
            ["first.bin"] = RandomNumberGenerator.GetBytes(65536 + 3),
            ["nested/empty.bin"] = [],
            ["nested/last.bin"] = RandomNumberGenerator.GetBytes(5)
        };
        foreach ((string relativePath, byte[] data) in files)
        {
            string path = Path.Combine(source, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllBytesAsync(path, data, cts.Token);
        }

        await using PifeonServer client = CreateClient();
        await using ISenderSession senderSession = await client.CreateSessionHandleAsync(cts.Token);
        ISender sender = await senderSession.GetSenderAsync(cts.Token);
        await using IReceiverSession receiverSession = await client.JoinSessionHandleAsync(senderSession.Code, cts.Token);
        IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
        var sent = new List<TransferProgress>();
        var received = new List<TransferProgress>();
        sender.OnTransferProgressChanged += sent.Add;
        receiver.OnTransferProgressChanged += received.Add;

        TransferManifest manifest = await TransferAsync(
            sender, receiver, senderSession.Code, source, destination, cts.Token);

        var expected = new List<TransferProgress>();
        long totalBytesTransferred = 0;
        for (int itemIndex = 0; itemIndex < manifest.Items.Count; itemIndex++)
        {
            TransferItemInfo item = manifest.Items[itemIndex];
            long fileBytesTransferred = 0;
            if (item.FileSize == 0)
            {
                expected.Add(new TransferProgress(
                    itemIndex + 1,
                    manifest.TotalFiles,
                    item.RelativePath,
                    0,
                    0,
                    totalBytesTransferred,
                    manifest.TotalSizeBytes,
                    IsFileCompleted: true,
                    IsTransferCompleted: itemIndex == manifest.Items.Count - 1));
                continue;
            }

            while (fileBytesTransferred < item.FileSize)
            {
                long chunkBytes = Math.Min(65536, item.FileSize - fileBytesTransferred);
                fileBytesTransferred += chunkBytes;
                totalBytesTransferred += chunkBytes;
                expected.Add(new TransferProgress(
                    itemIndex + 1,
                    manifest.TotalFiles,
                    item.RelativePath,
                    fileBytesTransferred,
                    item.FileSize,
                    totalBytesTransferred,
                    manifest.TotalSizeBytes,
                    fileBytesTransferred == item.FileSize,
                    fileBytesTransferred == item.FileSize && itemIndex == manifest.Items.Count - 1));
            }
        }

        Assert.Equal(expected, sent);
        Assert.Equal(sent, received);
        foreach ((string relativePath, byte[] data) in files)
        {
            string path = Path.Combine(destination, relativePath.Replace('/', Path.DirectorySeparatorChar));
            Assert.Equal(data, await File.ReadAllBytesAsync(path, cts.Token));
        }
    }

    [Fact]
    public async Task EmptyFolderTransfer_ShouldReportCompletionOnBothPeers()
    {
        using CancellationTokenSource cts = CreateTimeout();
        string source = Path.Combine(_tempDirectory, "empty-source");
        string destination = Path.Combine(_tempDirectory, "received");
        Directory.CreateDirectory(source);
        await using PifeonServer client = CreateClient();
        await using ISenderSession senderSession = await client.CreateSessionHandleAsync(cts.Token);
        ISender sender = await senderSession.GetSenderAsync(cts.Token);
        await using IReceiverSession receiverSession = await client.JoinSessionHandleAsync(senderSession.Code, cts.Token);
        IReceiver receiver = await receiverSession.GetReceiverAsync(cts.Token);
        var sent = new List<TransferProgress>();
        var received = new List<TransferProgress>();
        sender.OnTransferProgressChanged += sent.Add;
        receiver.OnTransferProgressChanged += received.Add;

        TransferManifest manifest = await TransferAsync(
            sender, receiver, senderSession.Code, source, destination, cts.Token);

        TransferProgress completed = new(0, 0, string.Empty, 0, 0, 0, 0, false, true);
        Assert.Empty(manifest.Items);
        Assert.Equal(new[] { completed }, sent);
        Assert.Equal(sent, received);
    }

    [Fact]
    public async Task CompatibilityApi_ShouldUseDirectTransferAndOwnSessionLifetime()
    {
        using CancellationTokenSource cts = CreateTimeout();
        await using PifeonServer client = CreateClient();
        await using ISender sender = await client.CreateSessionAsync(cts.Token);
        await using IReceiver receiver = await client.JoinSessionAsync(sender.Code, cts.Token);
        await AssertSignalingSessionRemovedAsync(sender.Code, cts.Token);
        string file = Path.Combine(_tempDirectory, "compatibility.bin");
        byte[] bytes = RandomNumberGenerator.GetBytes(65537);
        await File.WriteAllBytesAsync(file, bytes, cts.Token);
        string destination = Path.Combine(_tempDirectory, "received");

        await TransferAsync(sender, receiver, sender.Code, file, destination, cts.Token);

        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(destination, "compatibility.bin"), cts.Token));
    }
}
