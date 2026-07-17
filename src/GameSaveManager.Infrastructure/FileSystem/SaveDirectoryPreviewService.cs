using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;
using GameSaveManager.Application;

namespace GameSaveManager.Infrastructure.FileSystem;

/// <summary>与 Manifest 使用同一规则扫描文件，但绝不计算哈希或读取内容。</summary>
public sealed class SaveDirectoryPreviewService(ISaveDirectoryScanner scanner) : ISaveDirectoryPreviewService
{
    public async Task<SaveDirectoryPreview> PreviewAsync(SaveRootRule rule, CancellationToken cancellationToken)
        => await PreviewAsync(rule, GameSaveProtocolLimits.MaximumManifestFiles + 1, cancellationToken);

    private async Task<SaveDirectoryPreview> PreviewAsync(
        SaveRootRule rule,
        int maximumFiles,
        CancellationToken cancellationToken)
    {
        SaveDirectoryScanResult scan = await scanner.ScanWithBudgetAsync(
            rule, SaveDirectoryScanBudget.CreateDefault(maximumFiles), cancellationToken);
        IReadOnlyList<ScannedSaveFile> files = scan.Files;
        long total = files.Sum(file => file.Size);
        DateTime? latest = files.Count == 0 ? null : files.Max(file => file.LastWriteTimeUtc);
        string[] recent = files.OrderByDescending(file => file.LastWriteTimeUtc).Take(5).Select(file => file.RelativePath).ToArray();
        string[] largest = files.OrderByDescending(file => file.Size).Take(5).Select(file => file.RelativePath).ToArray();
        var warnings = new List<string>();
        if (files.Count == 0) warnings.Add("目录为空，确认后同步不会上传任何文件。");
        if (files.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            warnings.Add(GameSaveProtocolLimits.ManifestFileLimitMessage);
        if (scan.WasTruncated)
            warnings.Add(scan.TruncationReason ?? "扫描达到安全预算，当前数字表示至少已发现的数量。");
        if (total >= 5L * 1024 * 1024 * 1024) warnings.Add("目录超过 5 GB，请确认没有包含无关内容。");
        else if (total >= 1024L * 1024 * 1024) warnings.Add("目录超过 1 GB，请确认没有包含无关内容。");
        return new SaveDirectoryPreview(files.Count, total, latest, recent, largest, warnings,
            rule.IncludePatterns, rule.ExcludePatterns, scan.WasTruncated,
            scan.VisitedFileCount, scan.VisitedDirectoryCount);
    }

    public async Task<SaveProfilePreview> PreviewProfileAsync(
        IReadOnlyList<SaveRootRule> roots,
        IReadOnlyList<RegistrySaveRule> registryRules,
        CancellationToken cancellationToken)
    {
        if (roots.Count == 0) throw new InvalidOperationException("至少需要一个存档目录。");
        SaveRootTopologyValidator.Validate(roots);
        var previews = new List<SaveRootPreview>(roots.Count);
        var warnings = new List<string>();
        int totalFiles = 0;
        long totalSize = 0;
        DateTime? latest = null;
        foreach (SaveRootRule rule in roots.OrderBy(item => item.RootId, StringComparer.OrdinalIgnoreCase))
        {
            int remainingUntilOverflow = Math.Max(1,
                GameSaveProtocolLimits.MaximumManifestFiles + 1 - totalFiles);
            SaveDirectoryPreview preview = await PreviewAsync(rule, remainingUntilOverflow, cancellationToken);
            previews.Add(new SaveRootPreview(rule, preview.FileCount, preview.TotalSize,
                preview.LatestWriteTimeUtc, preview.Warnings, preview.WasTruncated,
                preview.VisitedFileCount, preview.VisitedDirectoryCount));
            totalFiles = checked(totalFiles + preview.FileCount);
            totalSize = checked(totalSize + preview.TotalSize);
            if (preview.LatestWriteTimeUtc is { } rootLatest
                && (latest is null || rootLatest > latest.Value)) latest = rootLatest;
            warnings.AddRange(preview.Warnings.Select(message => $"{rule.RootId}：{message}"));
            if (totalFiles > GameSaveProtocolLimits.MaximumManifestFiles) break;
        }
        if (totalFiles > GameSaveProtocolLimits.MaximumManifestFiles
            && !warnings.Contains(GameSaveProtocolLimits.ManifestFileLimitMessage, StringComparer.Ordinal))
            warnings.Add(GameSaveProtocolLimits.ManifestFileLimitMessage);
        if (registryRules.Any(rule => !rule.KeyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
                                      && !rule.KeyPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)))
            warnings.Add("存在不受支持的注册表路径，仅允许 HKCU。");
        return new SaveProfilePreview(previews, totalFiles, totalSize, latest, warnings,
            SaveProfileFingerprint.Create(roots, registryRules));
    }
}
