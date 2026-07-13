using GameSaveManager.Application.Files;

namespace GameSaveManager.Infrastructure.FileSystem;

/// <summary>完整递归扫描存档目录中的普通文件，并跳过符号链接/Junction 等 Reparse Point。</summary>
public sealed class SaveDirectoryScanner : ISaveDirectoryScanner
{
    public Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(
        string saveDirectory,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory))
        {
            throw new ArgumentException("存档目录不能为空", nameof(saveDirectory));
        }

        string root = Path.GetFullPath(saveDirectory);
        if (!Directory.Exists(root))
        {
            throw new DirectoryNotFoundException($"存档目录不存在: {root}");
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
