using GameSaveManager.Application.Files;
using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>SQLite 持久化 SHA-256 缓存；size/mtime 只用于判断缓存是否仍可复用。</summary>
public sealed class SqliteFileHashCache(SqliteDatabase database) : IFileHashCache
{
    public async Task<string?> TryGetAsync(
        string filePath,
        long size,
        DateTime lastWriteTimeUtc,
        CancellationToken cancellationToken)
    {
        string normalizedPath = Path.GetFullPath(filePath);
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT sha256
            FROM file_hash_cache
            WHERE path = $path
              AND size = $size
              AND last_write_time_utc_ticks = $ticks
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$path", normalizedPath);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$ticks", lastWriteTimeUtc.Ticks);
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value as string;
    }

    public async Task UpsertAsync(
        string filePath,
        long size,
        DateTime lastWriteTimeUtc,
        string sha256,
        CancellationToken cancellationToken)
    {
        string normalizedPath = Path.GetFullPath(filePath);
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO file_hash_cache(path, size, last_write_time_utc_ticks, sha256)
            VALUES($path, $size, $ticks, $sha256)
            ON CONFLICT(path) DO UPDATE SET
                size = excluded.size,
                last_write_time_utc_ticks = excluded.last_write_time_utc_ticks,
                sha256 = excluded.sha256;
            """;
        command.Parameters.AddWithValue("$path", normalizedPath);
        command.Parameters.AddWithValue("$size", size);
        command.Parameters.AddWithValue("$ticks", lastWriteTimeUtc.Ticks);
        command.Parameters.AddWithValue("$sha256", sha256);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
