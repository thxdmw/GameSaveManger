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
        string fullDestination = Path.GetFullPath(destinationZip);
        Directory.CreateDirectory(Path.GetDirectoryName(fullDestination)!);
        if (File.Exists(fullDestination)) throw new IOException($"导出文件已存在: {fullDestination}");

        using ZipArchive archive = ZipFile.Open(fullDestination, ZipArchiveMode.Create);
        foreach (CloudSnapshotFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ValidateRelativePath(file.RelativePath);
            string cached = await objectCache.GetOrDownloadAsync(file,
                (temporary, token) => apiClient.DownloadObjectAsync(server, deviceToken, file.ObjectId, temporary, token),
                cancellationToken);
            string hash = await fileHashService.ComputeSha256Async(cached, cancellationToken);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"导出前对象校验失败: {file.RelativePath}");
            ZipArchiveEntry entry = archive.CreateEntry(file.RelativePath.Replace('\\', '/'), CompressionLevel.Optimal);
            await using Stream source = File.OpenRead(cached);
            await using Stream target = entry.Open();
            await source.CopyToAsync(target, cancellationToken);
        }
        return fullDestination;
    }

    private static void ValidateRelativePath(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || Path.IsPathRooted(value)
            || value.Replace('\\', '/').Split('/').Any(part => part == ".."))
            throw new InvalidDataException("快照包含不安全的导出路径");
    }
}