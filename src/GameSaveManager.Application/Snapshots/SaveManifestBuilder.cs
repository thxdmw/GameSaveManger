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
        if (roots.Any(root => !root.UserConfirmed)) throw new InvalidOperationException("所有存档根目录都必须经用户确认。");
        if (roots.Select(root => root.RootId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != roots.Count) throw new InvalidOperationException("存档根目录标识不能重复。");
        var manifest = new List<SnapshotFile>();
        foreach (SaveRootRule root in roots)
        {
            IReadOnlyList<ScannedSaveFile> files = await scanner.ScanAsync(root, cancellationToken);
            foreach (ScannedSaveFile file in files.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (manifest.Count >= GameSaveProtocolLimits.MaximumManifestFiles)
                    throw new InvalidOperationException(GameSaveProtocolLimits.ManifestFileLimitMessage);
                string? sha256 = await hashCache.TryGetAsync(file.FullPath, file.Size, file.LastWriteTimeUtc, cancellationToken);
                if (sha256 is null)
                {
                    sha256 = await hashService.ComputeSha256Async(file.FullPath, cancellationToken);
                    EnsureFileStayedStable(file);
                    await hashCache.UpsertAsync(file.FullPath, file.Size, file.LastWriteTimeUtc, sha256, cancellationToken);
                }
                EnsureFileStayedStable(file);
                manifest.Add(new SnapshotFile($"{root.RootId}/{file.RelativePath}", sha256, file.Size));
            }
        }
        if (manifest.Select(file => file.RelativePath).Distinct(StringComparer.OrdinalIgnoreCase).Count() != manifest.Count) throw new InvalidOperationException("多个存档根目录生成了重复文件路径。");
        return manifest.OrderBy(file => file.RelativePath, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static void EnsureFileStayedStable(ScannedSaveFile file)
    {
        var current = new FileInfo(file.FullPath);
        if (!current.Exists || current.Length != file.Size || current.LastWriteTimeUtc != file.LastWriteTimeUtc) throw new IOException($"存档文件在构建清单时发生变化：{file.RelativePath}");
    }
}
