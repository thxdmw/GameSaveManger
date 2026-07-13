using GameSaveManager.Application.Games;
using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>SQLite 本机游戏配置存储；服务器标识与游戏 ID 共同构成隔离键。</summary>
public sealed class SqliteLocalGameProfileStore(SqliteDatabase database) : ILocalGameProfileStore
{
    public async Task<LocalGameProfile?> GetAsync(
        string serverKey,
        string gameId,
        CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT save_directory, process_name, auto_snapshot_enabled
            FROM local_game_profile
            WHERE server_key = $serverKey AND game_id = $gameId
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$serverKey", serverKey);
        command.Parameters.AddWithValue("$gameId", gameId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;

        return new LocalGameProfile(
            serverKey,
            gameId,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt64(2) != 0);
    }

    public async Task DeleteAsync(string serverKey, string gameId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM local_game_profile WHERE server_key = $serverKey AND game_id = $gameId;";
        command.Parameters.AddWithValue("$serverKey", serverKey);
        command.Parameters.AddWithValue("$gameId", gameId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO local_game_profile(
                server_key, game_id, save_directory, process_name, auto_snapshot_enabled, update_time_utc)
            VALUES($serverKey, $gameId, $saveDirectory, $processName, $enabled, $updatedAt)
            ON CONFLICT(server_key, game_id) DO UPDATE SET
                save_directory = excluded.save_directory,
                process_name = excluded.process_name,
                auto_snapshot_enabled = excluded.auto_snapshot_enabled,
                update_time_utc = excluded.update_time_utc;
            """;
        command.Parameters.AddWithValue("$serverKey", profile.ServerKey);
        command.Parameters.AddWithValue("$gameId", profile.GameId);
        command.Parameters.AddWithValue("$saveDirectory", profile.SaveDirectory);
        command.Parameters.AddWithValue("$processName", profile.ProcessName);
        command.Parameters.AddWithValue("$enabled", profile.AutoSnapshotEnabled ? 1 : 0);
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}