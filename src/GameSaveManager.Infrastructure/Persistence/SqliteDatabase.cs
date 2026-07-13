using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>客户端 SQLite 连接与事务化 schema 迁移入口。</summary>
public sealed class SqliteDatabase
{
    private const long CurrentSchemaVersion = 2;
    private readonly string _connectionString;

    public SqliteDatabase(string? databasePath = null)
    {
        AppDataPaths.EnsureCreated();
        string path = databasePath ?? AppDataPaths.DatabasePath;
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public SqliteConnection CreateConnection() => new(_connectionString);

    /// <summary>
    /// 启用 WAL，并在一个事务中创建或迁移本地数据库。
    /// 若数据库版本比当前客户端新，立即失败以避免旧客户端误写未知 schema。
    /// </summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (SqliteCommand pragmaCommand = connection.CreateCommand())
        {
            pragmaCommand.CommandText = "PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA foreign_keys = ON;";
            await pragmaCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        try
        {
            await EnsureSchemaVersionTableAsync(connection, transaction, cancellationToken);
            long version = await ReadSchemaVersionAsync(connection, transaction, cancellationToken);
            if (version > CurrentSchemaVersion)
            {
                throw new InvalidOperationException(
                    $"本地数据库版本 {version} 高于当前客户端支持的版本 {CurrentSchemaVersion}，请升级客户端后重试。");
            }

            await EnsureBaseTablesAsync(connection, transaction, cancellationToken);
            await EnsureSyncStateSchemaAsync(connection, transaction, cancellationToken);
            await EnsureLocalGameProfileSchemaAsync(connection, transaction, cancellationToken);

            if (version < CurrentSchemaVersion)
            {
                await WriteSchemaVersionAsync(connection, transaction, CurrentSchemaVersion, cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }

    private static async Task EnsureSchemaVersionTableAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS schema_version (
                id INTEGER NOT NULL PRIMARY KEY CHECK (id = 1),
                version INTEGER NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<long> ReadSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT version FROM schema_version WHERE id = 1;";
        object? value = await command.ExecuteScalarAsync(cancellationToken);
        return value is null or DBNull ? 0 : Convert.ToInt64(value);
    }

    private static async Task WriteSchemaVersionAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        long version,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO schema_version(id, version)
            VALUES(1, $version)
            ON CONFLICT(id) DO UPDATE SET version = excluded.version;
            """;
        command.Parameters.AddWithValue("$version", version);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task EnsureBaseTablesAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS file_hash_cache (
                path TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                size INTEGER NOT NULL,
                last_write_time_utc_ticks INTEGER NOT NULL,
                sha256 TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS client_setting (
                setting_key TEXT NOT NULL PRIMARY KEY,
                setting_value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// 第一版实验表仅以 game_id 为主键，无法隔离多个 GameSave 服务端。
    /// 旧表缺少 server_key 时直接重建；旧 HEAD 无法可靠推断所属服务端，禁止猜测迁移。
    /// </summary>
    private static async Task EnsureSyncStateSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        bool tableExists;
        await using (SqliteCommand existsCommand = connection.CreateCommand())
        {
            existsCommand.Transaction = transaction;
            existsCommand.CommandText = """
                SELECT COUNT(*)
                FROM sqlite_master
                WHERE type = 'table' AND name = 'sync_state';
                """;
            object? count = await existsCommand.ExecuteScalarAsync(cancellationToken);
            tableExists = Convert.ToInt32(count) > 0;
        }

        if (tableExists)
        {
            bool hasServerKey;
            await using (SqliteCommand columnCommand = connection.CreateCommand())
            {
                columnCommand.Transaction = transaction;
                columnCommand.CommandText = """
                    SELECT COUNT(*)
                    FROM pragma_table_info('sync_state')
                    WHERE name = 'server_key';
                    """;
                object? count = await columnCommand.ExecuteScalarAsync(cancellationToken);
                hasServerKey = Convert.ToInt32(count) > 0;
            }

            if (!hasServerKey)
            {
                await using SqliteCommand dropCommand = connection.CreateCommand();
                dropCommand.Transaction = transaction;
                dropCommand.CommandText = "DROP TABLE sync_state;";
                await dropCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using SqliteCommand createCommand = connection.CreateCommand();
        createCommand.Transaction = transaction;
        createCommand.CommandText = """
            CREATE TABLE IF NOT EXISTS sync_state (
                server_key TEXT NOT NULL,
                game_id TEXT NOT NULL,
                head_snapshot_id TEXT NULL,
                head_version INTEGER NOT NULL,
                PRIMARY KEY(server_key, game_id)
            );
            """;
        await createCommand.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>创建按服务端隔离的本地游戏配置表。</summary>
    private static async Task EnsureLocalGameProfileSchemaAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS local_game_profile (
                server_key TEXT NOT NULL,
                game_id TEXT NOT NULL,
                save_directory TEXT NOT NULL,
                process_name TEXT NOT NULL,
                auto_snapshot_enabled INTEGER NOT NULL,
                update_time_utc INTEGER NOT NULL,
                PRIMARY KEY(server_key, game_id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}