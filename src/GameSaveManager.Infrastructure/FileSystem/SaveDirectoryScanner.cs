using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;

namespace GameSaveManager.Infrastructure.FileSystem;

/// <summary>递归扫描已确认存档根目录，跳过重解析点，并统一应用包含和排除规则。</summary>
public sealed class SaveDirectoryScanner : ISaveDirectoryScanner
{
    public Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(string saveDirectory, CancellationToken cancellationToken) =>
        ScanAsync(SaveRootRule.CreateDefault(saveDirectory, Application.Discovery.SaveLocationSource.Manual, 100, true), cancellationToken);

    public Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(SaveRootRule rule, CancellationToken cancellationToken)
    {
        SaveRuleMatcher.Validate(rule);
        string root = Path.GetFullPath(rule.Path);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"存档目录不存在: {root}");
        return Task.Run<IReadOnlyList<ScannedSaveFile>>(() =>
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
            var result = new List<ScannedSaveFile>();
            foreach (string path in Directory.EnumerateFiles(root, "*", options))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var info = new FileInfo(path);
                string relative = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
                if (!SaveRuleMatcher.Includes(rule, relative)) continue;
                result.Add(new ScannedSaveFile(relative, info.FullName, info.Length, info.LastWriteTimeUtc));
            }
            return result;
        }, cancellationToken);
    }
}
