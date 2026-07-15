using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

public sealed class SqliteDatabase
{
    private const long CurrentSchemaVersion = 5;
    private readonly string _connectionString;

    public SqliteDatabase(string? databasePath = null)
    {
        AppDataPaths.EnsureCreated();
        _connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath ?? AppDataPaths.DatabasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
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
        catch { await transaction.RollbackAsync(CancellationToken.None); throw; }
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
            long matches = await ScalarLongAsync(connection, transaction, "SELECT COUNT(*) FROM pragma_table_info('local_game_profile') WHERE name IN ('provider', 'user_confirmed', 'save_directory_source', 'save_roots_json', 'registry_save_rules_json');", cancellationToken);
            if (matches != 5) await ExecuteAsync(connection, transaction, "DROP TABLE local_game_profile;", cancellationToken);
        }
        await ExecuteAsync(connection, transaction, """
            CREATE TABLE IF NOT EXISTS local_game_profile (
                server_key TEXT NOT NULL, game_id TEXT NOT NULL, provider TEXT NOT NULL, provider_game_id TEXT NULL,
                install_directory TEXT NULL, save_directory TEXT NOT NULL, process_name TEXT NOT NULL, executable_path TEXT NULL,
                save_directory_source TEXT NOT NULL, save_directory_confidence INTEGER NOT NULL, save_roots_json TEXT NOT NULL, registry_save_rules_json TEXT NOT NULL, user_confirmed INTEGER NOT NULL,
                auto_snapshot_enabled INTEGER NOT NULL, update_time_utc INTEGER NOT NULL, PRIMARY KEY(server_key, game_id)
            );
            """, cancellationToken);
    }

    private static async Task<long> ScalarLongAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using SqliteCommand command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
        foreach ((string name, object value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
