using GameSaveManager.Application.Api;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Sync;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.Diagnostics;
using Microsoft.Data.Sqlite;

var failures = new List<string>();

Run("localhost HTTP 可以用于本地开发", () =>
{
    Uri uri = GameSaveServerIdentity.ParseAndValidate("http://localhost:8080");
    Check(uri.IsLoopback, "localhost 应识别为回环地址");
});

Run("远程 HTTP 必须被拒绝", () =>
{
    ExpectThrows<InvalidOperationException>(
        () => GameSaveServerIdentity.ParseAndValidate("http://192.0.2.1:8080"));
});

Run("服务端基础地址禁止 Query 和 Fragment", () =>
{
    ExpectThrows<InvalidOperationException>(
        () => GameSaveServerIdentity.ParseAndValidate("https://example.com/game-save?tenant=a"));
    ExpectThrows<InvalidOperationException>(
        () => GameSaveServerIdentity.ParseAndValidate("https://example.com/game-save#fragment"));
});

Run("scheme 和 host 大小写不影响服务端稳定标识", () =>
{
    string first = GameSaveServerIdentity.CreateStableKey(new Uri("https://EXAMPLE.com/GameSave/"));
    string second = GameSaveServerIdentity.CreateStableKey(new Uri("https://example.com/GameSave"));
    Check(first == second, "同一服务端应生成相同稳定标识");
});

Run("服务端基础路径大小写必须保持隔离", () =>
{
    string upperPath = GameSaveServerIdentity.CreateStableKey(new Uri("https://example.com/GameSave"));
    string lowerPath = GameSaveServerIdentity.CreateStableKey(new Uri("https://example.com/gamesave"));
    Check(upperPath != lowerPath, "大小写不同的基础路径不能共享 Token 或同步状态");
});

await RunAsync("旧 sync_state 表迁移并按服务端隔离 HEAD", VerifySyncStateMigrationAsync);
await RunAsync("SQLite schema 版本迁移可重复执行", SqliteSchemaVerification.VerifySchemaVersionAsync);
await RunAsync("本机游戏配置按服务端隔离", VerifyLocalGameProfileAsync);
await RunAsync("旧本机游戏配置可迁移 EXE 路径字段", VerifyLocalGameProfileMigrationAsync);
await RunAsync("安全重试只重试 GET 请求", RetryAndLoggingVerification.VerifySafeRetryHandlerAsync);
await RunAsync("结构化日志会脱敏凭据", RetryAndLoggingVerification.VerifyJsonFileLoggerAsync);
Run("CMS 无时区日期与时间戳可以兼容解析", CmsDateTimeOffsetVerification.Verify);
Run("Desktop UI starts without WPF binding errors", GameSaveManager.Verification.WpfSmokeVerification.VerifyMainWindowLoadsWithoutBindingErrors);

if (failures.Count > 0)
{
    Console.Error.WriteLine($"验证失败，共 {failures.Count} 项：");
    foreach (string failure in failures)
    {
        Console.Error.WriteLine($"- {failure}");
    }
    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("GameSave Manager 基础边界验证全部通过。");

void Run(string name, Action action)
{
    try
    {
        action();
        Console.WriteLine($"[通过] {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}：{exception.Message}");
    }
}

async Task RunAsync(string name, Func<Task> action)
{
    try
    {
        await action();
        Console.WriteLine($"[通过] {name}");
    }
    catch (Exception exception)
    {
        failures.Add($"{name}：{exception.Message}");
    }
}

async Task VerifyLocalGameProfileAsync()
{
    string tempDirectory = Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    string databasePath = Path.Combine(tempDirectory, "profiles.db");
    try
    {
        var database = new SqliteDatabase(databasePath);
        await database.InitializeAsync(CancellationToken.None);
        var store = new SqliteLocalGameProfileStore(database);
        await store.SaveAsync(new LocalGameProfile("server-a", "same-game", "D:\\Saves\\A", "game-a.exe", "D:\\Games\\A\\game-a.exe", true), CancellationToken.None);
        await store.SaveAsync(new LocalGameProfile("server-b", "same-game", "E:\\Saves\\B", "game-b.exe", null, false), CancellationToken.None);
        LocalGameProfile? serverA = await store.GetAsync("server-a", "same-game", CancellationToken.None);
        LocalGameProfile? serverB = await store.GetAsync("server-b", "same-game", CancellationToken.None);
        Check(serverA is { SaveDirectory: "D:\\Saves\\A", ProcessName: "game-a.exe", ExecutablePath: "D:\\Games\\A\\game-a.exe", AutoSnapshotEnabled: true }, "server-a local profile read failed");
        Check(serverB is { SaveDirectory: "E:\\Saves\\B", ProcessName: "game-b.exe", ExecutablePath: null, AutoSnapshotEnabled: false }, "server-b local profile read failed");
    }
    finally
    {
        TryDelete(databasePath);
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
        try { Directory.Delete(tempDirectory, recursive: true); } catch (IOException) { }
    }
}
async Task VerifyLocalGameProfileMigrationAsync()
{
    string tempDirectory = Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    string databasePath = Path.Combine(tempDirectory, "profiles-migration.db");
    try
    {
        await using (var connection = new SqliteConnection($"Data Source={databasePath}"))
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE local_game_profile (
                    server_key TEXT NOT NULL,
                    game_id TEXT NOT NULL,
                    save_directory TEXT NOT NULL,
                    process_name TEXT NOT NULL,
                    auto_snapshot_enabled INTEGER NOT NULL,
                    update_time_utc INTEGER NOT NULL,
                    PRIMARY KEY(server_key, game_id)
                );
                """;
            await command.ExecuteNonQueryAsync();
        }

        var database = new SqliteDatabase(databasePath);
        await database.InitializeAsync(CancellationToken.None);
        await using SqliteConnection verificationConnection = database.CreateConnection();
        await verificationConnection.OpenAsync();
        await using SqliteCommand verificationCommand = verificationConnection.CreateCommand();
        verificationCommand.CommandText = "PRAGMA table_info(local_game_profile);";
        await using SqliteDataReader reader = await verificationCommand.ExecuteReaderAsync();
        bool migrated = false;
        while (await reader.ReadAsync()) migrated |= string.Equals(reader.GetString(1), "executable_path", StringComparison.OrdinalIgnoreCase);
        Check(migrated, "旧本机游戏配置未添加 executable_path 字段");
    }
    finally
    {
        TryDelete(databasePath);
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
        try { Directory.Delete(tempDirectory, recursive: true); } catch (IOException) { }
    }
}
async Task VerifySyncStateMigrationAsync()
{
    string tempDirectory = Path.Combine(
        Path.GetTempPath(),
        "GameSaveManager.Verification",
        Guid.NewGuid().ToString("N"));
    Directory.CreateDirectory(tempDirectory);
    string databasePath = Path.Combine(tempDirectory, "verification.db");

    try
    {
        var database = new SqliteDatabase(databasePath);

        // 模拟第一版实验数据库：sync_state 只有 game_id，没有服务端作用域。
        await using (SqliteConnection connection = database.CreateConnection())
        {
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE sync_state (
                    game_id TEXT NOT NULL PRIMARY KEY,
                    head_snapshot_id TEXT NULL,
                    head_version INTEGER NOT NULL
                );
                INSERT INTO sync_state(game_id, head_snapshot_id, head_version)
                VALUES('legacy-game', 'legacy-head', 7);
                """;
            await command.ExecuteNonQueryAsync();
        }

        await database.InitializeAsync(CancellationToken.None);

        await using (SqliteConnection connection = database.CreateConnection())
        {
            await connection.OpenAsync();

            await using SqliteCommand columnCommand = connection.CreateCommand();
            columnCommand.CommandText = """
                SELECT COUNT(*)
                FROM pragma_table_info('sync_state')
                WHERE name = 'server_key';
                """;
            long serverKeyColumnCount = Convert.ToInt64(await columnCommand.ExecuteScalarAsync());
            Check(serverKeyColumnCount == 1, "迁移后的 sync_state 必须包含 server_key");

            await using SqliteCommand rowCommand = connection.CreateCommand();
            rowCommand.CommandText = "SELECT COUNT(*) FROM sync_state;";
            long migratedRows = Convert.ToInt64(await rowCommand.ExecuteScalarAsync());
            Check(migratedRows == 0, "无法判断所属服务端的旧 HEAD 必须丢弃，禁止猜测迁移");
        }

        var store = new SqliteSyncStateStore(database);
        await store.SaveAsync(
            new LocalSyncState("server-a", "same-game", "head-a", 1),
            CancellationToken.None);
        await store.SaveAsync(
            new LocalSyncState("server-b", "same-game", "head-b", 9),
            CancellationToken.None);

        LocalSyncState? serverA = await store.GetAsync(
            "server-a", "same-game", CancellationToken.None);
        LocalSyncState? serverB = await store.GetAsync(
            "server-b", "same-game", CancellationToken.None);

        Check(serverA?.HeadSnapshotId == "head-a" && serverA.HeadVersion == 1,
            "server-a HEAD 读取错误");
        Check(serverB?.HeadSnapshotId == "head-b" && serverB.HeadVersion == 9,
            "server-b HEAD 读取错误");
    }
    finally
    {
        TryDelete(databasePath);
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
        try
        {
            Directory.Delete(tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // CI 结束后临时目录会由 runner 清理；Windows SQLite 句柄释放延迟不应掩盖真正的验证结果。
        }
    }
}

void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

void ExpectThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}");
}

void TryDelete(string path)
{
    try
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    catch (IOException)
    {
        // 临时验证文件删除失败不影响业务边界断言。
    }
}
