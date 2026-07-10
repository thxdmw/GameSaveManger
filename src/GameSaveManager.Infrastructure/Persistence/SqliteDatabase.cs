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

    /// <summary>启用 WAL，并创建当前 V2 所需的最小本地持久化表。</summary>
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA synchronous = NORMAL;

            CREATE TABLE IF NOT EXISTS file_hash_cache (
                path TEXT NOT NULL PRIMARY KEY COLLATE NOCASE,
                size INTEGER NOT NULL,
                last_write_time_utc_ticks INTEGER NOT NULL,
                sha256 TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS sync_state (
                game_id TEXT NOT NULL PRIMARY KEY,
                head_snapshot_id TEXT NULL,
                head_version INTEGER NOT NULL
            );

            CREATE TABLE IF NOT EXISTS client_setting (
                setting_key TEXT NOT NULL PRIMARY KEY,
                setting_value TEXT NOT NULL
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
