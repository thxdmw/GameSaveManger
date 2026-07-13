namespace GameSaveManager.Application.Files;

/// <summary>文件 SHA-256 缓存契约；缓存实现不能改变 Manifest 的最终事实判断规则。</summary>
public interface IFileHashCache
{
    Task<string?> TryGetAsync(
        string fullPath,
        long size,
        DateTime lastWriteTimeUtc,
        CancellationToken cancellationToken);

    Task UpsertAsync(
        string fullPath,
        long size,
        DateTime lastWriteTimeUtc,
        string sha256,
        CancellationToken cancellationToken);
}
