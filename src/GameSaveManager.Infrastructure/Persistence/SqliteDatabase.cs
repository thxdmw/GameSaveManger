using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    private const long CurrentSchemaVersion = 9;
    private readonly string _connectionString;
    private readonly string _databasePath;

    public SqliteDatabase(string? databasePath = null)
    {
        AppDataPaths.EnsureCreated();
        _databasePath = databasePath ?? AppDataPaths.DatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public SqliteConnection CreateConnection() => new(_connectionString);

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using (SqliteCommand pragma = connection.CreateCommand())
        {
            pragma.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
            await pragma.ExecuteNonQueryAsync(cancellationToken);
        }

        if (await RequiresMigrationBackupAsync(connection, cancellationToken))
            await CreateMigrationBackupAsync(connection, cancellationToken);

        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS schema_version (id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1), version INTEGER NOT NULL);", cancellationToken);
            await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS file_hash_cache (path TEXT NOT NULL PRIMARY KEY COLLATE NOCASE, size INTEGER NOT NULL, last_write_time_utc_ticks INTEGER NOT NULL, sha256 TEXT NOT NULL); CREATE TABLE IF NOT EXISTS client_setting (setting_key TEXT NOT NULL PRIMARY KEY, setting_value TEXT NOT NULL);", cancellationToken);
            await EnsureCurrentSyncStateSchemaAsync(connection, transaction, cancellationToken);
            await EnsureCurrentLocalProfileSchemaAsync(connection, transaction, cancellationToken);
            await ExecuteAsync(connection, transaction, "INSERT INTO schema_version(id, version) VALUES(1, $version) ON CONFLICT(id) DO UPDATE SET version = excluded.version;", cancellationToken, ("$version", CurrentSchemaVersion));
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }

        await ValidateSchemaAsync(connection, cancellationToken);
    }

    private static async Task<bool> RequiresMigrationBackupAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'local_game_profile';";
        bool localProfileExists = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        bool localProfileMigrationRequired = false;
        if (localProfileExists)
        {
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('local_game_profile') WHERE name IN ('launch_profile_json', 'account_id');";
            localProfileMigrationRequired = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 2;
            if (!localProfileMigrationRequired)
            {
                command.CommandText = """
                    SELECT COUNT(*) FROM pragma_table_info('local_game_profile')
                    WHERE (name = 'server_key' AND pk = 1)
                       OR (name = 'account_id' AND pk = 2)
                       OR (name = 'game_id' AND pk = 3);
                    """;
                localProfileMigrationRequired = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 3;
            }
        }

        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'sync_state';";
        bool syncStateExists = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) > 0;
        bool syncStateMigrationRequired = false;
        if (syncStateExists)
        {
            command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('sync_state') WHERE name IN ('server_key', 'account_id');";
            syncStateMigrationRequired = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 2;
            if (!syncStateMigrationRequired)
            {
                command.CommandText = """
                    SELECT COUNT(*) FROM pragma_table_info('sync_state')
                    WHERE (name = 'server_key' AND pk = 1)
                       OR (name = 'account_id' AND pk = 2)
                       OR (name = 'game_id' AND pk = 3);
                    """;
                syncStateMigrationRequired = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 3;
            }
        }
        return localProfileMigrationRequired || syncStateMigrationRequired;
    }

    private async Task CreateMigrationBackupAsync(SqliteConnection source, CancellationToken cancellationToken)
    {
        if (!File.Exists(_databasePath)) return;
        string backupPath = _databasePath + ".backup-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff");
        await using var backup = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = backupPath,
            Mode = SqliteOpenMode.ReadWriteCreate
        }.ToString());
        await backup.OpenAsync(cancellationToken);
        source.BackupDatabase(backup);
    }

    private static async Task EnsureCurrentSyncStateSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        bool exists = await ScalarLongAsync(connection, transaction, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'sync_state';", cancellationToken) > 0;
        if (exists)
        {
            long hasServerKey = await ScalarLongAsync(connection, transaction, "SELECT COUNT(*) FROM pragma_table_info('sync_state') WHERE name = 'server_key';", cancellationToken);
            if (hasServerKey == 0) await ExecuteAsync(connection, transaction, "DROP TABLE sync_state;", cancellationToken);
            else
            {
                long hasAccountId = await ScalarLongAsync(connection, transaction, "SELECT COUNT(*) FROM pragma_table_info('sync_state') WHERE name = 'account_id';", cancellationToken);
                long accountScopedPrimaryKeyColumns = hasAccountId == 0
                    ? 0
                    : await ScalarLongAsync(connection, transaction, """
                        SELECT COUNT(*) FROM pragma_table_info('sync_state')
                        WHERE (name = 'server_key' AND pk = 1)
                           OR (name = 'account_id' AND pk = 2)
                           OR (name = 'game_id' AND pk = 3);
                        """, cancellationToken);
                if (hasAccountId == 0 || accountScopedPrimaryKeyColumns != 3)
                {
                    await ExecuteAsync(connection, transaction, "ALTER TABLE sync_state RENAME TO sync_state_legacy;", cancellationToken);
                    await ExecuteAsync(connection, transaction, "CREATE TABLE sync_state (server_key TEXT NOT NULL, account_id TEXT NOT NULL, game_id TEXT NOT NULL, head_snapshot_id TEXT NULL, head_version INTEGER NOT NULL, PRIMARY KEY(server_key, account_id, game_id));", cancellationToken);
                    string accountExpression = hasAccountId == 0 ? "''" : "COALESCE(account_id, '')";
                    await ExecuteAsync(connection, transaction, $"INSERT OR REPLACE INTO sync_state(server_key, account_id, game_id, head_snapshot_id, head_version) SELECT server_key, {accountExpression}, game_id, head_snapshot_id, head_version FROM sync_state_legacy;", cancellationToken);
                    await ExecuteAsync(connection, transaction, "DROP TABLE sync_state_legacy;", cancellationToken);
                }
            }
        }
        await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS sync_state (server_key TEXT NOT NULL, account_id TEXT NOT NULL, game_id TEXT NOT NULL, head_snapshot_id TEXT NULL, head_version INTEGER NOT NULL, PRIMARY KEY(server_key, account_id, game_id));", cancellationToken);
    }

    private static async Task EnsureCurrentLocalProfileSchemaAsync(SqliteConnection connection, SqliteTransaction transaction, CancellationToken cancellationToken)
    {
        bool exists = await ScalarLongAsync(connection, transaction, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'local_game_profile';", cancellationToken) > 0;
        if (exists)
        {
            await EnsureColumnAsync(connection, transaction, "provider", "TEXT NOT NULL DEFAULT 'CUSTOM'", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "provider_game_id", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "install_directory", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "executable_path", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "save_directory_source", "TEXT NOT NULL DEFAULT 'Manual'", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "save_directory_confidence", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "save_roots_json", "TEXT NOT NULL DEFAULT '[]'", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "registry_save_rules_json", "TEXT NOT NULL DEFAULT '[]'", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "user_confirmed", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "identity_executable_path", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "launch_profile_json", "TEXT NULL", cancellationToken);
            await EnsureColumnAsync(connection, transaction, "account_id", "TEXT NOT NULL DEFAULT ''", cancellationToken);
            await ExecuteAsync(connection, transaction, "UPDATE local_game_profile SET identity_executable_path = executable_path WHERE identity_executable_path IS NULL AND executable_path IS NOT NULL;", cancellationToken);
            await ExecuteAsync(connection, transaction, """
                UPDATE local_game_profile
                SET launch_profile_json = json_object(
                    'TargetType', 0,
                    'Target', executable_path,
                    'Arguments', NULL,
                    'WorkingDirectory', NULL,
                    'RunAsAdministrator', 0,
                    'MonitoredProcessNames', json_array(process_name))
                WHERE launch_profile_json IS NULL AND executable_path IS NOT NULL;
                """, cancellationToken);
            long accountScopedPrimaryKeyColumns = await ScalarLongAsync(connection, transaction, """
                SELECT COUNT(*) FROM pragma_table_info('local_game_profile')
                WHERE (name = 'server_key' AND pk = 1)
                   OR (name = 'account_id' AND pk = 2)
                   OR (name = 'game_id' AND pk = 3);
                """, cancellationToken);
            if (accountScopedPrimaryKeyColumns != 3)
            {
                await ExecuteAsync(connection, transaction, "ALTER TABLE local_game_profile RENAME TO local_game_profile_legacy;", cancellationToken);
                await ExecuteAsync(connection, transaction, CreateLocalGameProfileTableSql, cancellationToken);
                await ExecuteAsync(connection, transaction, """
                    INSERT INTO local_game_profile(server_key, account_id, game_id, provider, provider_game_id, install_directory,
                        save_directory, process_name, executable_path, save_directory_source, save_directory_confidence,
                        save_roots_json, registry_save_rules_json, user_confirmed, auto_snapshot_enabled,
                        identity_executable_path, launch_profile_json, update_time_utc)
                    SELECT server_key, account_id, game_id, provider, provider_game_id, install_directory,
                        save_directory, process_name, executable_path, save_directory_source, save_directory_confidence,
                        save_roots_json, registry_save_rules_json, user_confirmed, auto_snapshot_enabled,
                        identity_executable_path, launch_profile_json, update_time_utc
                    FROM local_game_profile_legacy;
                    """, cancellationToken);
                await ExecuteAsync(connection, transaction, "DROP TABLE local_game_profile_legacy;", cancellationToken);
            }
        }

        await ExecuteAsync(connection, transaction, CreateLocalGameProfileTableSql, cancellationToken);
        await ExecuteAsync(connection, transaction, "CREATE INDEX IF NOT EXISTS idx_local_game_profile_account ON local_game_profile(server_key, account_id, game_id);", cancellationToken);
    }

    private const string CreateLocalGameProfileTableSql = """
        CREATE TABLE IF NOT EXISTS local_game_profile (
            server_key TEXT NOT NULL, account_id TEXT NOT NULL DEFAULT '', game_id TEXT NOT NULL,
            provider TEXT NOT NULL, provider_game_id TEXT NULL, install_directory TEXT NULL,
            save_directory TEXT NOT NULL, process_name TEXT NOT NULL, executable_path TEXT NULL,
            save_directory_source TEXT NOT NULL, save_directory_confidence INTEGER NOT NULL,
            save_roots_json TEXT NOT NULL, registry_save_rules_json TEXT NOT NULL, user_confirmed INTEGER NOT NULL,
            auto_snapshot_enabled INTEGER NOT NULL, identity_executable_path TEXT NULL, launch_profile_json TEXT NULL,
            update_time_utc INTEGER NOT NULL, PRIMARY KEY(server_key, account_id, game_id)
        );
        """;

    private static async Task EnsureColumnAsync(SqliteConnection connection, SqliteTransaction transaction, string name, string declaration, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('local_game_profile') WHERE name = $name;";
        command.Parameters.AddWithValue("$name", name);
        long exists = Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
        if (exists == 0)
            await ExecuteAsync(connection, transaction, $"ALTER TABLE local_game_profile ADD COLUMN {name} {declaration};", cancellationToken);
    }

    private static async Task ValidateSchemaAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('local_game_profile') WHERE name IN ('executable_path', 'identity_executable_path', 'launch_profile_json', 'account_id');";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 4)
            throw new InvalidOperationException("本地游戏配置迁移后的结构校验失败。");
        command.CommandText = """
            SELECT COUNT(*) FROM pragma_table_info('local_game_profile')
            WHERE (name = 'server_key' AND pk = 1)
               OR (name = 'account_id' AND pk = 2)
               OR (name = 'game_id' AND pk = 3);
            """;
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 3)
            throw new InvalidOperationException("本地游戏配置的账号隔离主键校验失败。");
        command.CommandText = """
            SELECT COUNT(*) FROM pragma_table_info('sync_state')
            WHERE (name = 'server_key' AND pk = 1)
               OR (name = 'account_id' AND pk = 2)
               OR (name = 'game_id' AND pk = 3);
            """;
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 3)
            throw new InvalidOperationException("本地同步状态的账号隔离主键校验失败。");
        command.CommandText = "PRAGMA integrity_check;";
        if (!string.Equals(Convert.ToString(await command.ExecuteScalarAsync(cancellationToken)), "ok", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("本地数据库完整性校验失败。");
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
