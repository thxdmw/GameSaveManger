using GameSaveManager.Application.Api;
using GameSaveManager.Application.Files;

namespace GameSaveManager.Application.Restores;

/// <summary>
/// 本地内容寻址缓存。缓存命中与新下载均使用 SHA-256 和大小二次验证，
/// 因此即使临时文件中断或缓存损坏，也不会被用于恢复真实存档。
/// </summary>
public sealed class ContentObjectCache(IFileHashService fileHashService)
{
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameSaveManager",
        "objects");

    public async Task<string> GetOrDownloadAsync(
        CloudSnapshotFile file,
        Func<string, CancellationToken, Task> downloadAsync,
        CancellationToken cancellationToken)
    {
        ValidateDescriptor(file);
        string destination = GetCachePath(file.Sha256);
        if (await IsValidAsync(destination, file, cancellationToken))
        {
            return destination;
        }

        if (File.Exists(destination))
        {
            File.Delete(destination);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);

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
            if (File.Exists(temporary))
            {
                File.Delete(temporary);
            }
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
        string actualHash = await fileHashService.ComputeSha256Async(path, cancellationToken);
        return string.Equals(actualHash, expected.Sha256, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetCachePath(string sha256) => Path.Combine(CacheRoot, sha256[..2], sha256);

    private static void ValidateDescriptor(CloudSnapshotFile file)
    {
        if (string.IsNullOrWhiteSpace(file.ObjectId)
            || file.Size < 0
            || file.Sha256.Length != 64
            || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
        {
            throw new InvalidDataException("云端对象描述不合法，已拒绝写入本地缓存");
        }
    }
}