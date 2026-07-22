using GameSaveManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

internal static class SqliteSchemaVerification
{
    public static async Task VerifySchemaVersionAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "schema.db");
        try
        {
            var database = new SqliteDatabase(databasePath);
            await database.InitializeAsync(CancellationToken.None);
            await database.InitializeAsync(CancellationToken.None);

            await using SqliteConnection connection = database.CreateConnection();
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM schema_version WHERE id = 1;";
            long version = Convert.ToInt64(await command.ExecuteScalarAsync());
            Ensure(version == 9, "本地数据库应被记录为当前 schema 版本。");

            await VerifyLegacyClaimCollisionAsync(database, connection);
            await VerifyIntermediateSyncStateSchemaAsync(directory);
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task VerifyLegacyClaimCollisionAsync(
        SqliteDatabase database,
        SqliteConnection connection)
    {
        const string serverKey = "server-a";
        const string userId = "user-a";
        const string gameId = "game-a";
        await using (SqliteCommand seed = connection.CreateCommand())
        {
            seed.CommandText = """
                INSERT INTO sync_state(server_key, account_id, game_id, head_snapshot_id, head_version)
                VALUES($serverKey, '', $gameId, 'legacy-head', 1),
                      ($serverKey, $userId, $gameId, 'current-head', 2);
                INSERT INTO local_game_profile(
                    server_key, account_id, game_id, provider, save_directory, process_name,
                    save_directory_source, save_directory_confidence, save_roots_json,
                    registry_save_rules_json, user_confirmed, auto_snapshot_enabled, update_time_utc)
                VALUES
                    ($serverKey, '', $gameId, 'CUSTOM', 'C:\Legacy', 'game.exe',
                     'Manual', 100, '[]', '[]', 1, 0, 1),
                    ($serverKey, $userId, $gameId, 'CUSTOM', 'C:\Current', 'game.exe',
                     'Manual', 100, '[]', '[]', 1, 0, 2);
                """;
            seed.Parameters.AddWithValue("$serverKey", serverKey);
            seed.Parameters.AddWithValue("$userId", userId);
            seed.Parameters.AddWithValue("$gameId", gameId);
            await seed.ExecuteNonQueryAsync();
        }

        var syncStore = new SqliteSyncStateStore(database);
        var profileStore = new SqliteLocalGameProfileStore(database);
        GameSaveManager.Application.Sync.LocalSyncState? state = await syncStore.GetAsync(
            serverKey, userId, gameId, CancellationToken.None);
        await profileStore.ClaimLegacyAsync(
            serverKey, userId, [gameId], CancellationToken.None);
        Ensure(state?.HeadSnapshotId == "current-head",
            "账号已有同步基线时，旧版未归属基线不得覆盖当前账号数据。");

        await using SqliteCommand verify = connection.CreateCommand();
        verify.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM sync_state
                 WHERE server_key = $serverKey AND account_id = '' AND game_id = $gameId),
                (SELECT COUNT(*) FROM local_game_profile
                 WHERE server_key = $serverKey AND account_id = '' AND game_id = $gameId),
                (SELECT save_directory FROM local_game_profile
                 WHERE server_key = $serverKey AND account_id = $userId AND game_id = $gameId);
            """;
        verify.Parameters.AddWithValue("$serverKey", serverKey);
        verify.Parameters.AddWithValue("$userId", userId);
        verify.Parameters.AddWithValue("$gameId", gameId);
        await using SqliteDataReader reader = await verify.ExecuteReaderAsync();
        Ensure(await reader.ReadAsync()
               && reader.GetInt64(0) == 0
               && reader.GetInt64(1) == 0
               && reader.GetString(2) == @"C:\Current",
            "旧版未归属记录与当前账号记录冲突时，应保留当前账号版本并安全清理旧记录。");
    }

    private static async Task VerifyIntermediateSyncStateSchemaAsync(string directory)
    {
        string path = Path.Combine(directory, "intermediate.db");
        await using (var connection = new SqliteConnection($"Data Source={path}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand seed = connection.CreateCommand();
            seed.CommandText = """
                CREATE TABLE sync_state (
                    server_key TEXT NOT NULL,
                    account_id TEXT NOT NULL DEFAULT '',
                    game_id TEXT NOT NULL,
                    head_snapshot_id TEXT NULL,
                    head_version INTEGER NOT NULL,
                    PRIMARY KEY(server_key, game_id));
                INSERT INTO sync_state(server_key, account_id, game_id, head_snapshot_id, head_version)
                VALUES('server-b', 'user-b', 'game-b', 'head-b', 7);
                """;
            await seed.ExecuteNonQueryAsync();
        }

        var database = new SqliteDatabase(path);
        await database.InitializeAsync(CancellationToken.None);
        await using SqliteConnection migrated = database.CreateConnection();
        await migrated.OpenAsync();
        await using SqliteCommand verify = migrated.CreateCommand();
        verify.CommandText = """
            SELECT
                (SELECT COUNT(*) FROM pragma_table_info('sync_state')
                 WHERE (name = 'server_key' AND pk = 1)
                    OR (name = 'account_id' AND pk = 2)
                    OR (name = 'game_id' AND pk = 3)),
                (SELECT COUNT(*) FROM sync_state
                 WHERE server_key = 'server-b' AND account_id = 'user-b'
                   AND game_id = 'game-b' AND head_snapshot_id = 'head-b' AND head_version = 7);
            """;
        await using SqliteDataReader reader = await verify.ExecuteReaderAsync();
        Ensure(await reader.ReadAsync() && reader.GetInt64(0) == 3 && reader.GetInt64(1) == 1,
            "带 account_id 但主键仍为旧结构的中间版本必须重建主键并保留原数据。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}
