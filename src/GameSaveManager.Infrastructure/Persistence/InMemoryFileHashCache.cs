using System.Collections.Concurrent;
using GameSaveManager.Application.Files;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>
/// V2 bootstrap cache. The contract is persistence-agnostic; the next persistence step replaces this with SQLite.
/// </summary>
public sealed class InMemoryFileHashCache : IFileHashCache
{
    private readonly ConcurrentDictionary<string, CacheEntry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> TryGetAsync(
        string fullPath,
        long size,
        DateTime lastWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = Path.GetFullPath(fullPath);
        if (_entries.TryGetValue(key, out CacheEntry? entry)
            && entry.Size == size
            && entry.LastWriteTimeUtc == lastWriteTimeUtc)
        {
            return Task.FromResult<string?>(entry.Sha256);
        }

        return Task.FromResult<string?>(null);
    }

    public Task UpsertAsync(
        string fullPath,
        long size,
        DateTime lastWriteTimeUtc,
        string sha256,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string key = Path.GetFullPath(fullPath);
        _entries[key] = new CacheEntry(size, lastWriteTimeUtc, sha256);
        return Task.CompletedTask;
    }

    private sealed record CacheEntry(long Size, DateTime LastWriteTimeUtc, string Sha256);
}
