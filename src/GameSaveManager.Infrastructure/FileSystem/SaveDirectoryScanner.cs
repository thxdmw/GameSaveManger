using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;
using GameSaveManager.Application;
using System.Diagnostics;

namespace GameSaveManager.Infrastructure.FileSystem;

/// <summary>递归扫描已确认存档根目录，跳过重解析点，并统一应用包含和排除规则。</summary>
public sealed class SaveDirectoryScanner : ISaveDirectoryScanner
{
    public Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(string saveDirectory, CancellationToken cancellationToken) =>
        ScanAsync(SaveRootRule.CreateDefault(saveDirectory, Application.Discovery.SaveLocationSource.Manual, 100, true), cancellationToken);

    public Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(SaveRootRule rule, CancellationToken cancellationToken)
        => ScanAsync(rule, GameSaveProtocolLimits.MaximumManifestFiles + 1, cancellationToken);

    public async Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(
        SaveRootRule rule,
        int maximumFiles,
        CancellationToken cancellationToken)
    {
        SaveDirectoryScanResult scan = await ScanWithBudgetAsync(
            rule, SaveDirectoryScanBudget.CreateDefault(maximumFiles), cancellationToken);
        if (scan.WasTruncated && scan.Files.Count < maximumFiles)
            throw new InvalidOperationException(scan.TruncationReason ?? "存档目录扫描超过安全预算。");
        return scan.Files;
    }

    public Task<SaveDirectoryScanResult> ScanWithBudgetAsync(
        SaveRootRule rule,
        SaveDirectoryScanBudget budget,
        CancellationToken cancellationToken)
    {
        ValidateBudget(budget);
        SaveRuleMatcher.Validate(rule);
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rule.Path));
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"存档目录不存在: {root}");
        SaveRootTopologyValidator.ValidateNoReparsePointTraversal(root);
        return Task.Run(() =>
        {
            var stopwatch = Stopwatch.StartNew();
            var pendingDirectories = new Queue<string>();
            pendingDirectories.Enqueue(root);
            var result = new List<ScannedSaveFile>();
            int visitedFiles = 0;
            int visitedDirectories = 1;
            string? truncationReason = null;
            while (pendingDirectories.Count > 0 && truncationReason is null)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (stopwatch.Elapsed > budget.MaximumDuration)
                {
                    truncationReason = $"扫描超过 {budget.MaximumDuration.TotalSeconds:0} 秒，结果仅为部分预览。";
                    break;
                }
                string directory = pendingDirectories.Dequeue();
                try
                {
                    foreach (string path in Directory.EnumerateFiles(directory, "*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (++visitedFiles > budget.MaximumVisitedFiles)
                        {
                            truncationReason = $"已访问超过 {budget.MaximumVisitedFiles} 个文件，结果仅为部分预览。";
                            break;
                        }
                        if (stopwatch.Elapsed > budget.MaximumDuration)
                        {
                            truncationReason = $"扫描超过 {budget.MaximumDuration.TotalSeconds:0} 秒，结果仅为部分预览。";
                            break;
                        }
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                        var info = new FileInfo(path);
                        string relative = Path.GetRelativePath(root, info.FullName).Replace('\\', '/');
                        if (!SaveRuleMatcher.Includes(rule, relative)) continue;
                        result.Add(new ScannedSaveFile(relative, info.FullName, info.Length, info.LastWriteTimeUtc));
                        if (result.Count >= budget.MaximumMatchedFiles)
                        {
                            truncationReason = $"匹配文件已达到 {budget.MaximumMatchedFiles} 个，结果仅为部分预览。";
                            break;
                        }
                    }
                    if (truncationReason is not null) break;
                    foreach (string child in Directory.EnumerateDirectories(directory, "*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                        if (++visitedDirectories > budget.MaximumVisitedDirectories)
                        {
                            truncationReason = $"已访问超过 {budget.MaximumVisitedDirectories} 个目录，结果仅为部分预览。";
                            break;
                        }
                        pendingDirectories.Enqueue(child);
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
                {
                    // 与原 IgnoreInaccessible 语义一致；单个不可访问目录不会中断其余预览。
                }
            }
            return new SaveDirectoryScanResult(result, Math.Min(visitedFiles, budget.MaximumVisitedFiles),
                Math.Min(visitedDirectories, budget.MaximumVisitedDirectories), truncationReason is not null,
                truncationReason);
        }, cancellationToken);
    }

    private static void ValidateBudget(SaveDirectoryScanBudget budget)
    {
        if (budget.MaximumMatchedFiles <= 0) throw new ArgumentOutOfRangeException(nameof(budget));
        if (budget.MaximumVisitedFiles <= 0 || budget.MaximumVisitedDirectories <= 0
            || budget.MaximumDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(budget), "扫描预算必须全部大于零。");
    }
}
