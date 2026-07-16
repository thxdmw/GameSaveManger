using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    private const long CurrentSchemaVersion = 6;
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

        if (await RequiresLaunchProfileMigrationAsync(connection, cancellationToken))
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

    private static async Task<bool> RequiresLaunchProfileMigrationAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'local_game_profile';";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0) return false;
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('local_game_profile') WHERE name = 'launch_profile_json';";
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) == 0;
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
        }
        await ExecuteAsync(connection, transaction, "CREATE TABLE IF NOT EXISTS sync_state (server_key TEXT NOT NULL, game_id TEXT NOT NULL, head_snapshot_id TEXT NULL, head_version INTEGER NOT NULL, PRIMARY KEY(server_key, game_id));", cancellationToken);
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
        }

        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS local_game_profile (
                server_key TEXT NOT NULL, game_id TEXT NOT NULL, provider TEXT NOT NULL, provider_game_id TEXT NULL,
                install_directory TEXT NULL, save_directory TEXT NOT NULL, process_name TEXT NOT NULL, executable_path TEXT NULL,
                save_directory_source TEXT NOT NULL, save_directory_confidence INTEGER NOT NULL, save_roots_json TEXT NOT NULL, registry_save_rules_json TEXT NOT NULL, user_confirmed INTEGER NOT NULL,
                auto_snapshot_enabled INTEGER NOT NULL, identity_executable_path TEXT NULL, launch_profile_json TEXT NULL, update_time_utc INTEGER NOT NULL, PRIMARY KEY(server_key, game_id)
            );
            """, cancellationToken);
    }

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
        command.CommandText = "SELECT COUNT(*) FROM pragma_table_info('local_game_profile') WHERE name IN ('executable_path', 'identity_executable_path', 'launch_profile_json');";
        if (Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken)) != 3)
            throw new InvalidOperationException("本地游戏配置迁移后的结构校验失败。");
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