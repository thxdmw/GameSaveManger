using GameSaveManager.Application.Api;
using GameSaveManager.Application.Files;

namespace GameSaveManager.Application.Restores;

/// <summary>
/// 本地内容寻址缓存。缓存命中与新下载均使用 SHA-256 和大小二次验证，
/// 因此即使临时文件中断或缓存损坏，也不会被用于恢复真实存档。
/// </summary>
public sealed class ContentObjectCache
{
    private readonly IFileHashService _fileHashService;
    private readonly string _cacheRoot;
    private readonly string _cacheBoundaryRoot;

    public ContentObjectCache(IFileHashService fileHashService, string? cacheRoot = null)
    {
        _fileHashService = fileHashService;
        string applicationRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameSaveManager");
        _cacheRoot = Path.GetFullPath(cacheRoot ?? Path.Combine(applicationRoot, "objects"));
        _cacheBoundaryRoot = Path.GetFullPath(cacheRoot is null ? applicationRoot : _cacheRoot);
    }

    public async Task<string> GetOrDownloadAsync(
        CloudSnapshotFile file,
        Func<string, CancellationToken, Task> downloadAsync,
        CancellationToken cancellationToken)
    {
        ValidateDescriptor(file);
        string destination = GetCachePath(file.Sha256);
        EnsureSafeCachePath(destination);
        if (await IsValidAsync(destination, file, cancellationToken))
        {
            TryTouch(destination);
            return destination;
        }

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        EnsureSafeCachePath(destination);
        EnsureAvailableSpace(destination, file.Size);

        string temporary = destination + ".partial-" + Guid.NewGuid().ToString("N");
        try
        {
            await downloadAsync(temporary, cancellationToken);
            if (!await IsValidAsync(temporary, file, cancellationToken))
            {
                throw new InvalidDataException($"下载对象校验失败: {file.ObjectId}");
            }

            try
            {
                File.Move(temporary, destination, overwrite: false);
            }
            catch (IOException)
            {
                if (!await IsValidAsync(destination, file, cancellationToken))
                {
                    throw;
                }
                // 其他恢复任务已下载同一内容；验证通过后安全复用它的缓存文件。
            }
            return destination;
        }
        finally
        {
            TryDeleteTemporaryFile(temporary);
        }
    }

    private async Task<bool> IsValidAsync(
        string path,
        CloudSnapshotFile expected,
        CancellationToken cancellationToken)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expected.Size)
        {
            return false;
        }
        string actualHash = await _fileHashService.ComputeSha256Async(path, cancellationToken);
        return string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private string GetCachePath(string sha256) => Path.Combine(_cacheRoot, sha256[..2], sha256);

    private void EnsureSafeCachePath(string path)
    {
        string boundary = Path.TrimEndingDirectorySeparator(_cacheBoundaryRoot);
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string relative = Path.GetRelativePath(boundary, target);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("对象缓存路径越过应用数据边界，已停止读写。");

        string current = boundary;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries).Prepend(string.Empty))
        {
            if (segment.Length > 0) current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("对象缓存路径包含重解析点，已停止读写。");
        }
    }

    private static void EnsureAvailableSpace(string destination, long expectedSize)
    {
        string root = Path.GetPathRoot(Path.GetFullPath(destination)) ?? throw new IOException("无法确定对象缓存磁盘。");
        long reserve = Math.Max(100L * 1024 * 1024, expectedSize / 10);
        long required = checked(expectedSize + reserve);
        long available = new DriveInfo(root).AvailableFreeSpace;
        if (available < required)
            throw new IOException($"对象缓存空间不足：至少需要 {required} 字节，当前只有 {available} 字节。");
    }

    private static void TryTouch(string path)
    {
        try { File.SetLastAccessTimeUtc(path, DateTime.UtcNow); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteTemporaryFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void ValidateDescriptor(CloudSnapshotFile file)
    {
        if (string.IsNullOrWhiteSpace(file.ObjectId)
            || file.Size < 0
            || string.IsNullOrWhiteSpace(file.Sha256)
            || file.Sha256.Length != 64
            || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("云端对象描述不合法，已拒绝写入本地缓存");
        }
    }
}
