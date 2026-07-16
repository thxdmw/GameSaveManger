using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;

namespace GameSaveManager.Infrastructure.FileSystem;

/// <summary>与 Manifest 使用同一规则扫描文件，但绝不计算哈希或读取内容。</summary>
public sealed class SaveDirectoryPreviewService(ISaveDirectoryScanner scanner) : ISaveDirectoryPreviewService
{
    public async Task<SaveDirectoryPreview> PreviewAsync(SaveRootRule rule, CancellationToken cancellationToken)
    {
        IReadOnlyList<ScannedSaveFile> files = await scanner.ScanAsync(rule, cancellationToken);
        long total = files.Sum(file => file.Size);
        DateTime? latest = files.Count == 0 ? null : files.Max(file => file.LastWriteTimeUtc);
        string[] recent = files.OrderByDescending(file => file.LastWriteTimeUtc).Take(5).Select(file => file.RelativePath).ToArray();
        string[] largest = files.OrderByDescending(file => file.Size).Take(5).Select(file => file.RelativePath).ToArray();
        var warnings = new List<string>();
        if (files.Count == 0) warnings.Add("目录为空，确认后同步不会上传任何文件。");
        if (files.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            warnings.Add(GameSaveProtocolLimits.ManifestFileLimitMessage);
        if (total >= 5L * 1024 * 1024 * 1024) warnings.Add("目录超过 5 GB，请确认没有包含无关内容。");
        else if (total >= 1024L * 1024 * 1024) warnings.Add("目录超过 1 GB，请确认没有包含无关内容。");
        return new SaveDirectoryPreview(files.Count, total, latest, recent, largest, warnings, rule.IncludePatterns, rule.ExcludePatterns);
    }
}
