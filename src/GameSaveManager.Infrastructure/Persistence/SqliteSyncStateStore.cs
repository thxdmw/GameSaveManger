using GameSaveManager.Application.Sync;
using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>持久化每个服务端、每个游戏最后一次成功同步后确认的云端 HEAD。</summary>
public sealed class SqliteSyncStateStore(SqliteDatabase database) : ILocalSyncStateStore
{
    public async Task<LocalSyncState?> GetAsync(
        string serverKey,
        string gameId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT head_snapshot_id, head_version
            FROM sync_state
            WHERE server_key = $serverKey AND game_id = $gameId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$serverKey", serverKey);
        command.Parameters.AddWithValue("$gameId", gameId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        string? headSnapshotId = reader.IsDBNull(0) ? null : reader.GetString(0);
        return new LocalSyncState(serverKey, gameId, headSnapshotId, reader.GetInt64(1));
    }

    public async Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO sync_state(server_key, game_id, head_snapshot_id, head_version)
            VALUES($serverKey, $gameId, $headSnapshotId, $headVersion)
            ON CONFLICT(server_key, game_id) DO UPDATE SET
                head_snapshot_id = excluded.head_snapshot_id,
                head_version = excluded.head_version;
            """;
        command.Parameters.AddWithValue("$serverKey", state.ServerKey);
        command.Parameters.AddWithValue("$gameId", state.GameId);
        command.Parameters.AddWithValue("$headSnapshotId", (object?)state.HeadSnapshotId ?? DBNull.Value);
        command.Parameters.AddWithValue("$headVersion", state.HeadVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task DeleteAsync(string serverKey, string gameId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM sync_state WHERE server_key = $serverKey AND game_id = $gameId;";
        command.Parameters.AddWithValue("$serverKey", serverKey);
        command.Parameters.AddWithValue("$gameId", gameId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
