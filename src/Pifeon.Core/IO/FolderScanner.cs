using Pifeon.Core.Abstractions;

namespace Pifeon.Core.IO;

public static class FolderScanner
{
    public static IEnumerable<TransferItem> ScanPath(string path)
    {
        if (File.Exists(path))
        {
            var fileInfo = new FileInfo(path);
            yield return new TransferItem(fileInfo.Name, fileInfo.Length, fileInfo.FullName);
        }
        else if (Directory.Exists(path))
        {
            var rootDir = new DirectoryInfo(path);
            foreach (var file in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
            {
                // Mantiene la struttura di sottocartelle relativa
                string relativePath = Path.GetRelativePath(rootDir.FullName, file.FullName);
                yield return new TransferItem(relativePath, file.Length, file.FullName);
            }
        }
        else
        {
            throw new FileNotFoundException($"Il percorso specificato non esiste: {path}");
        }
    }
}
