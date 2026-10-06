using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using Pifeon.Core.Abstractions;
using Pifeon.Core.IO;

namespace Pifeon.Tests.CoreTests;

public sealed class FolderScannerTests : IDisposable
{
    private readonly string _testDirectory;

    public FolderScannerTests()
    {
        // Crea una directory temporanea unica per ciascuna esecuzione del test
        _testDirectory = Path.Combine(Path.GetTempPath(), "Pifeon_FolderScannerTests_" + Guid.NewGuid());
        Directory.CreateDirectory(_testDirectory);
    }

    public void Dispose()
    {
        // Pulisce tutti i file e le cartelle temporanee create
        if (Directory.Exists(_testDirectory))
        {
            Directory.Delete(_testDirectory, recursive: true);
        }
    }

    #region Ramo 1: ScanPath con un File Singolo

    [Fact]
    public async Task ScanPath_ShouldReturnSingleItem_WhenPathIsFile()
    {
        // Arrange
        string filePath = Path.Combine(_testDirectory, "sample.txt");
        byte[] fileContent = "Hello Pifeon P2P"u8.ToArray();
        await File.WriteAllBytesAsync(filePath, fileContent, TestContext.Current.CancellationToken);

        string expectedHash = ComputeExpectedSha256(fileContent);

        // Act
        List<TransferItem> result = await FolderScanner.ScanPath(filePath).ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        TransferItem item = Assert.Single(result);
        Assert.Equal("sample.txt", item.RelativePath);
        Assert.Equal(fileContent.Length, item.FileSize);
        Assert.Equal(filePath, item.FullPath);
        Assert.Equal(expectedHash, item.Hash);
    }

    #endregion

    #region Ramo 2: ScanPath con una Directory (Inclusi i file nelle sottocartelle)

    [Fact]
    public async Task ScanPath_ShouldReturnAllItemsRecursively_WhenPathIsDirectoryAsync()
    {
        // Arrange: Crea struttura cartelle -> _testDirectory/file1.txt e _testDirectory/sub/file2.txt
        string file1Path = Path.Combine(_testDirectory, "file1.txt");
        byte[] file1Content = "Contenuto File 1"u8.ToArray();
        await File.WriteAllBytesAsync(file1Path, file1Content, TestContext.Current.CancellationToken);

        string subDir = Path.Combine(_testDirectory, "sub");
        Directory.CreateDirectory(subDir);
        string file2Path = Path.Combine(subDir, "file2.txt");
        byte[] file2Content = "Contenuto File 2 di prova"u8.ToArray();
        await File.WriteAllBytesAsync(file2Path, file2Content, TestContext.Current.CancellationToken);

        string expectedHash1 = ComputeExpectedSha256(file1Content);
        string expectedHash2 = ComputeExpectedSha256(file2Content);

        // Act
        List<TransferItem> result = await FolderScanner.ScanPath(_testDirectory).ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(2, result.Count);

        TransferItem item1 = result.First(i => i.RelativePath == "file1.txt");
        Assert.Equal(file1Content.Length, item1.FileSize);
        Assert.Equal(file1Path, item1.FullPath);
        Assert.Equal(expectedHash1, item1.Hash);

        // Verifica il percorso relativo per i file nelle sottocartelle
        string expectedSubRelativePath = Path.Combine("sub", "file2.txt");
        TransferItem item2 = result.First(i => i.RelativePath == expectedSubRelativePath);
        Assert.Equal(file2Content.Length, item2.FileSize);
        Assert.Equal(file2Path, item2.FullPath);
        Assert.Equal(expectedHash2, item2.Hash);
    }

    [Fact]
    public async Task ScanPath_ShouldReturnEmptySequence_WhenDirectoryIsEmptyAsync()
    {
        // Arrange: _testDirectory is already created and is empty

        // Act
        List<TransferItem> result = await FolderScanner.ScanPath(_testDirectory).ToListAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Empty(result);
    }

    #endregion

    #region Ramo 3: ScanPath con Percorso Non Esistente

    [Fact]
    public async Task ScanPath_ShouldThrowFileNotFoundException_WhenPathDoesNotExistAsync()
    {
        // Arrange
        string nonExistingPath = Path.Combine(_testDirectory, "non_existing_file_123.txt");

        // Act & Assert (Richiede l'enumerazione con .ToListAsync() per scatenare l'eccezione dello yield return)
        FileNotFoundException exception = await Assert.ThrowsAsync<FileNotFoundException>(async () =>
            await FolderScanner.ScanPath(nonExistingPath).ToListAsync(TestContext.Current.CancellationToken));

        Assert.Contains("Il percorso specificato non esiste", exception.Message);
        Assert.Contains(nonExistingPath, exception.Message);
    }

    #endregion

    #region Helper per il calcolo dell'Hash di verifica

    private static string ComputeExpectedSha256(byte[] data)
    {
        byte[] hash = SHA256.HashData(data);
        return Convert.ToHexStringLower(hash);
    }

    #endregion
}
