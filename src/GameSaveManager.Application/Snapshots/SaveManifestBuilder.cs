using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Application.Snapshots;

public sealed class SaveManifestBuilder(ISaveDirectoryScanner scanner, IFileHashService hashService, IFileHashCache hashCache)
{
    public Task<IReadOnlyList<SnapshotFile>> BuildAsync(string saveDirectory, CancellationToken cancellationToken) =>
        BuildAsync([SaveRootRule.CreateDefault(saveDirectory, Discovery.SaveLocationSource.Manual, 100, true)], cancellationToken);

    public async Task<IReadOnlyList<SnapshotFile>> BuildAsync(IReadOnlyList<SaveRootRule> roots, CancellationToken cancellationToken)
    {
        if (roots is null || roots.Count == 0) throw new InvalidOperationException("至少需要一个存档根目录。");
        if (roots.Count > GameSaveProtocolLimits.MaximumSnapshotRoots)
            throw new InvalidOperationException($"单个快照最多支持 {GameSaveProtocolLimits.MaximumSnapshotRoots} 个存档根目录。");
        if (roots.Any(root => root is null)) throw new InvalidOperationException("存档根目录不能包含空记录。");
        if (roots.Any(root => !root.UserConfirmed)) throw new InvalidOperationException("所有存档根目录都必须经用户确认。");
        if (roots.Select(root => root.RootId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != roots.Count) throw new InvalidOperationException("存档根目录标识不能重复。");
        foreach (SaveRootRule root in roots)
        {
            if (string.IsNullOrWhiteSpace(root.RootId)
                || root.RootId.Length > GameSaveProtocolLimits.RootIdMaxLength
                || root.RootId.Any(character => !char.IsAsciiLetterOrDigit(character)
                                                && character is not '_' and not '-'))
                throw new InvalidOperationException("存档根目录标识不符合云端协议要求。");
            if (!Enum.IsDefined(root.Source) || root.Confidence is < 0 or > 100)
                throw new InvalidOperationException($"存档根目录 {root.RootId} 的来源或置信度无效。");
            ValidatePatterns(root.IncludePatterns, root.RootId);
            ValidatePatterns(root.ExcludePatterns, root.RootId);
        }
        SaveRootTopologyValidator.Validate(roots);
        var manifest = new List<SnapshotFile>();
        foreach (SaveRootRule root in roots)
        {
            IReadOnlyList<ScannedSaveFile> files = await scanner.ScanAsync(root, cancellationToken);
            foreach (ScannedSaveFile file in files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (manifest.Count >= GameSaveProtocolLimits.MaximumManifestFiles)
                    throw new InvalidOperationException(GameSaveProtocolLimits.ManifestFileLimitMessage);
                // size/mtime 不是内容事实：部分程序会原位改写文件并保留时间戳。
                // 正式 Manifest 必须每次读取真实内容；缓存只用于避免重复写入相同结果。
                string? cachedSha256 = await hashCache.TryGetAsync(
                    file.FullPath, file.Size, file.LastWriteTimeUtc, cancellationToken);
                string sha256 = await hashService.ComputeSha256Async(file.FullPath, cancellationToken);
                EnsureFileStayedStable(file);
                if (!string.Equals(cachedSha256, sha256, StringComparison.OrdinalIgnoreCase))
                    await hashCache.UpsertAsync(
                        file.FullPath, file.Size, file.LastWriteTimeUtc, sha256, cancellationToken);
                manifest.Add(new SnapshotFile($"{root.RootId}/{file.RelativePath}", sha256, file.Size));
            }
        }
        if (manifest.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Count) throw new InvalidOperationException("多个存档根目录生成了重复文件路径。");
        return manifest.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void ValidatePatterns(IReadOnlyList<string>? patterns, string rootId)
    {
        if (patterns is null || patterns.Count > GameSaveProtocolLimits.MaximumPatternsPerRoot)
            throw new InvalidOperationException($"存档根目录 {rootId} 的扫描规则数量无效。");
        if (patterns.Any(pattern => string.IsNullOrWhiteSpace(pattern)
                                    || pattern.Length > GameSaveProtocolLimits.PatternMaxLength
                                    || pattern.IndexOf('\0') >= 0))
            throw new InvalidOperationException($"存档根目录 {rootId} 包含无效扫描规则。");
    }

    private static void EnsureFileStayedStable(ScannedSaveFile file)
    {
        var current = new FileInfo(file.FullPath);
        if (!current.Exists || current.Length != file.Size || current.LastWriteTimeUtc != file.LastWriteTimeUtc) throw new IOException($"存档文件在构建清单时发生变化：{file.RelativePath}");
    }
}
