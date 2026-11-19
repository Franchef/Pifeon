using System.Security.Cryptography;
using Pifeon.Core;
using Pifeon.Core.Abstractions;
using Pifeon.Core.Exceptions;

namespace Pifeon.AspireTests;

public sealed class TransferFlowTests(AppHostFixture fixture) : IClassFixture<AppHostFixture>
{
    private PifeonServer CreateClient()
    {
        var endpoint = new UriBuilder(fixture.ServerUri) { Scheme = "ws" };
        return new PifeonServer(endpoint.Uri.ToString());
    }

    private static CancellationTokenSource CreateTimeout()
    {
        var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        return cts;
    }

    private async Task<TransferManifest> TransferFolderAsync(
        string source, string destination, CancellationToken ct)
    {
        await using PifeonServer senderClient = CreateClient();
        await using PifeonServer receiverClient = CreateClient();
        await using ISenderSession senderSession = await senderClient.CreateSessionHandleAsync(ct);
        ISender sender = await senderSession.GetSenderAsync(ct);
        await using IReceiverSession receiverSession = await receiverClient
            .JoinSessionHandleAsync(senderSession.Code, ct);
        IReceiver receiver = await receiverSession.GetReceiverAsync(ct);
        await senderSession.WaitUntilConnectedAsync(ct);

        Assert.True(senderSession.IsConnected);
        Assert.True(receiverSession.IsConnected);

        async Task<TransferManifest> ReceiveAsync()
        {
            TransferManifest manifest = await receiver.ConnectAndGetManifestAsync(senderSession.Code, ct);
            await receiver.ReceiveToDirectoryAsync(destination, ct);
            return manifest;
        }

        Task<TransferManifest> receiving = ReceiveAsync();
        await Task.WhenAll(sender.SendAsync(source, ct), receiving);
        return await receiving;
    }

    [Fact]
    public async Task Receiver_WithInvalidSessionCode_ShouldReportSessionNotFound()
    {
        using CancellationTokenSource cts = CreateTimeout();
        await using PifeonServer receiverClient = CreateClient();

        SessionNotFoundException exception = await Assert.ThrowsAsync<SessionNotFoundException>(
            () => receiverClient.JoinSessionHandleAsync("0000000000", cts.Token));

        Assert.Equal("0000000000", exception.Code);
    }

    [Fact]
    public async Task FolderTransfer_ShouldDownloadKnownSizeFileWithIdenticalContents()
    {
        using CancellationTokenSource cts = CreateTimeout();
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"pifeon_aspire_{Guid.NewGuid():N}");
        string source = Path.Combine(tempDirectory, "Sender");
        string destination = Path.Combine(tempDirectory, "Receiver");
        Directory.CreateDirectory(source);

        try
        {
            int size = RandomNumberGenerator.GetInt32(65537, 2 * 1024 * 1024 + 1);
            byte[] expected = RandomNumberGenerator.GetBytes(size);
            string sourceFile = Path.Combine(source, "KnowSizeFile.test");
            await File.WriteAllBytesAsync(sourceFile, expected, cts.Token);

            TransferManifest manifest = await TransferFolderAsync(source, destination, cts.Token);

            TransferItemInfo item = Assert.Single(manifest.Items);
            Assert.Equal("KnowSizeFile.test", item.RelativePath);
            Assert.Equal(size, item.FileSize);
            Assert.Equal(1, manifest.TotalFiles);
            Assert.Equal(size, manifest.TotalSizeBytes);
            string downloadedFile = Assert.Single(Directory.GetFiles(destination, "*", SearchOption.AllDirectories));
            Assert.Equal(Path.Combine(destination, "KnowSizeFile.test"), downloadedFile);
            Assert.Equal(size, new FileInfo(downloadedFile).Length);
            Assert.Equal(expected, await File.ReadAllBytesAsync(downloadedFile, cts.Token));
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task FolderTransfer_WithRandomFiles_ShouldDownloadEveryFileToDestinationFolder()
    {
        using CancellationTokenSource cts = CreateTimeout();
        string tempDirectory = Path.Combine(Path.GetTempPath(), $"pifeon_aspire_{Guid.NewGuid():N}");
        string source = Path.Combine(tempDirectory, "Sender", "FolderToSend");
        string destination = Path.Combine(tempDirectory, "Receiver", "DestinationFolder");
        Directory.CreateDirectory(source);

        try
        {
            int fileCount = RandomNumberGenerator.GetInt32(2, 13);
            var expected = new Dictionary<string, byte[]>();
            for (int index = 0; index < fileCount; index++)
            {
                string fileName = $"file-{index}.test";
                int size = RandomNumberGenerator.GetInt32(index * 65536, (index + 1) * 65536);
                byte[] contents = RandomNumberGenerator.GetBytes(size);
                expected.Add(fileName, contents);
                await File.WriteAllBytesAsync(Path.Combine(source, fileName), contents, cts.Token);
            }

            TransferManifest manifest = await TransferFolderAsync(source, destination, cts.Token);

            Assert.Equal(fileCount, manifest.TotalFiles);
            Assert.Equal(expected.Values.Sum(contents => (long)contents.Length), manifest.TotalSizeBytes);
            Assert.Equal(
                expected.Keys.OrderBy(name => name),
                manifest.Items.Select(item => item.RelativePath).OrderBy(name => name));
            Assert.Equal(
                expected.Keys.OrderBy(name => name),
                Directory.GetFiles(destination, "*", SearchOption.AllDirectories)
                    .Select(path => Path.GetRelativePath(destination, path)).OrderBy(name => name));

            foreach ((string fileName, byte[] contents) in expected)
            {
                TransferItemInfo item = Assert.Single(manifest.Items, item => item.RelativePath == fileName);
                Assert.Equal(contents.Length, item.FileSize);
                string downloadedFile = Path.Combine(destination, fileName);
                Assert.Equal(contents.Length, new FileInfo(downloadedFile).Length);
                Assert.Equal(contents, await File.ReadAllBytesAsync(downloadedFile, cts.Token));
            }
        }
        finally
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
    }
}
