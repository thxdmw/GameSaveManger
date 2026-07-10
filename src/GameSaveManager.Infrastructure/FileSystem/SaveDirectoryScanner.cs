using GameSaveManager.Application.Files;

namespace GameSaveManager.Infrastructure.FileSystem;

public sealed class SaveDirectoryScanner : ISaveDirectoryScanner
{
    public Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(
        string saveDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
        {
            throw new ArgumentException("Save directory is required.", nameof(saveDirectory));
        }

        string root = Path.GetFullPath(saveDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"Save directory does not exist: {root}");
        }

        return Task.Run<IReadOnlyList<ScannedSaveFile>>(() =>
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = false,
                AttributesToSkip = FileAttributes.ReparsePoint,
                ReturnSpecialDirectories = false
            };

            var result = new List<ScannedSaveFile>();
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                string relativePath = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
                result.Add(new ScannedSaveFile(
                    relativePath,
                    info.FullName,
                    info.Length,
                    info.LastWriteTimeUtc));
            }

            return result;
        }, cancellationToken);
    }
}
