using System.IO.Compression;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Files;

namespace GameSaveManager.Application.Restores;

/// <summary>将一个不可变云端快照导出为 ZIP；内容仍逐个校验并复用本地对象缓存。</summary>
public sealed class SnapshotExportService(
    IGameSaveApiClient apiClient,
    ContentObjectCache objectCache,
    IFileHashService fileHashService)
{
    public async Task<string> ExportAsync(Uri server, string deviceToken, string gameId,
        string snapshotId, string destinationZip, CancellationToken cancellationToken)
    {
        CloudSnapshotManifest manifest = await apiClient.GetSnapshotAsync(server, deviceToken, gameId, snapshotId, cancellationToken);
        ValidateManifest(manifest, gameId, snapshotId);
        string fullDestination = Path.GetFullPath(destinationZip);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        if (File.Exists(fullDestination)) throw new IOException($"导出文件已存在: {fullDestination}");
        EnsureAvailableSpace(fullDestination, manifest.Files.Sum(file => file.Size));
        string temporary = fullDestination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            using (ZipArchive archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (CloudSnapshotFile file in manifest.Files)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ValidateRelativePath(file.RelativePath);
                    string cached = await objectCache.GetOrDownloadAsync(file,
                        (objectTemporary, token) => apiClient.DownloadObjectAsync(server, deviceToken, file.ObjectId, objectTemporary, file.Size, token),
                        cancellationToken);
                    string hash = await fileHashService.ComputeSha256Async(cached, cancellationToken);
                    if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException($"导出前对象校验失败: {file.RelativePath}");
                    ZipArchiveEntry entry = archive.CreateEntry(file.RelativePath.Replace('\\', '/'), CompressionLevel.Optimal);
                    await using Stream source = File.OpenRead(cached);
                    await using Stream target = entry.Open();
                    await source.CopyToAsync(target, cancellationToken);
                }
            }
            File.Move(temporary, fullDestination, overwrite: false);
            return fullDestination;
        }
        finally
        {
            TryDeleteTemporaryFile(temporary);
        }
    }

    private static void EnsureAvailableSpace(string destination, long logicalSize)
    {
        string root = Path.GetPathRoot(destination) ?? throw new IOException("无法确定导出目标磁盘。");
        long required = checked(logicalSize + Math.Max(100L * 1024 * 1024, logicalSize / 10));
        long available = new DriveInfo(root).AvailableFreeSpace;
        if (available < required)
            throw new IOException($"导出空间不足：至少需要 {required} 字节，当前只有 {available} 字节。");
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void ValidateRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > GameSaveProtocolLimits.RelativePathMaxLength
            || Path.IsPathRooted(value)
            || value.Replace('\\', '/').Split('/').Any(part => part == ".."))
            throw new InvalidDataException("快照包含不安全的导出路径");
    }

    private static void ValidateManifest(
        CloudSnapshotManifest manifest,
        string expectedGameId,
        string expectedSnapshotId)
    {
        CloudApiResponseValidator.ValidateManifest(manifest, expectedGameId, expectedSnapshotId);
        if (manifest.Files is null)
            throw new InvalidDataException("服务端返回的快照缺少文件清单。");
        if (!string.Equals(manifest.GameId, expectedGameId, StringComparison.Ordinal)
            || !string.Equals(manifest.SnapshotId, expectedSnapshotId, StringComparison.Ordinal))
            throw new InvalidDataException("服务端返回的快照身份与导出请求不一致。");
        if (manifest.Files.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            throw new InvalidDataException("服务端快照超过客户端允许的文件数量上限。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudSnapshotFile file in manifest.Files)
        {
            ValidateRelativePath(file.RelativePath);
            if (!paths.Add(file.RelativePath.Replace('\\', '/')))
                throw new InvalidDataException($"快照包含重复的导出路径：{file.RelativePath}");
            if (file.Size < 0 || string.IsNullOrWhiteSpace(file.ObjectId)
                || string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64
                || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"快照包含无效的内容描述：{file.RelativePath}");
        }
    }
}
