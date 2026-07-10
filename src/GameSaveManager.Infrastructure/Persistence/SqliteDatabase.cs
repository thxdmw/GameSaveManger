using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>客户端 SQLite 连接与基础表初始化入口。</summary>
public sealed class SqliteDatabase
{
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

    /// <summary>启用 WAL，并创建或迁移当前 V2 所需的最小本地持久化表。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                PRAGMA journal_mode = WAL;
                PRAGMA synchronous = NORMAL;

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

        await EnsureSyncStateSchemaAsync(connection, cancellationToken);
    }

    /// <summary>
    /// 第一版实验表仅以 game_id 为主键，无法隔离多个 GameSave 服务端。
    /// 旧表缺少 server_key 时直接重建；旧 HEAD 无法可靠推断所属服务端，禁止猜测迁移。
    /// </summary>
    private static async Task EnsureSyncStateSchemaAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        bool tableExists;
        await using (SqliteCommand existsCommand = connection.CreateCommand())
        {
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
                dropCommand.CommandText = "DROP TABLE sync_state;";
                await dropCommand.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        await using SqliteCommand createCommand = connection.CreateCommand();
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
}
