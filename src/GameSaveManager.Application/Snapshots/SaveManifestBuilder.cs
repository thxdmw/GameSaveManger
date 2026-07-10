using GameSaveManager.Application.Files;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Application.Snapshots;

/// <summary>
/// 构建存档内容清单。目录扫描是事实来源；mtime/size 只用于命中 SHA-256 缓存。
/// 文件在 Hash 期间发生变化时拒绝生成半一致 Manifest，由上层稍后重新扫描。
/// </summary>
public sealed class SaveManifestBuilder(
    ISaveDirectoryScanner scanner,
    IFileHashService hashService,
    IFileHashCache hashCache)
{
    public async Task<IReadOnlyList<SnapshotFile>> BuildAsync(
        string saveDirectory,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<ScannedSaveFile> scannedFiles =
            await scanner.ScanAsync(saveDirectory, cancellationToken);

        var manifest = new List<SnapshotFile>(scannedFiles.Count);
        foreach (ScannedSaveFile file in scannedFiles.OrderBy(item => item.RelativePath, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string? sha256 = await hashCache.TryGetAsync(
                file.FullPath,
                file.Size,
                file.LastWriteTimeUtc,
                cancellationToken);

            if (sha256 is null)
            {
                sha256 = await hashService.ComputeSha256Async(file.FullPath, cancellationToken);
                EnsureFileStayedStable(file);
                await hashCache.UpsertAsync(
                    file.FullPath,
                    file.Size,
                    file.LastWriteTimeUtc,
                    sha256,
                    cancellationToken);
            }

            manifest.Add(new SnapshotFile(file.RelativePath, sha256, file.Size));
        }

        return manifest;
    }

    private static void EnsureFileStayedStable(ScannedSaveFile scannedFile)
    {
        var current = new FileInfo(scannedFile.FullPath);
        if (!current.Exists
            || current.Length != scannedFile.Size
            || current.LastWriteTimeUtc != scannedFile.LastWriteTimeUtc)
        {
            throw new IOException($"Save file changed while hashing and must be rescanned: {scannedFile.RelativePath}");
        }
    }
}
