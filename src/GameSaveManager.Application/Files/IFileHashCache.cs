namespace GameSaveManager.Application.Files;

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
