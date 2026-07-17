using GameSaveManager.Application.Api;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Sync;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Restores;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.Diagnostics;
using GameSaveManager.Infrastructure.Discovery;
using Microsoft.Win32;
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
await RunAsync("旧本机游戏配置可迁移 EXE 路径字段", VerifyLocalGameProfileSchemaResetAsync);
await RunAsync("HKCU 注册表存档可安全往返", VerifyRegistrySnapshotAsync);
await RunAsync("Glob 与注册表 JSON 会进入多根 Manifest", VerifyGlobAndRegistryManifestAsync);
await RunAsync("多根恢复崩溃后按磁盘事实回滚且不重复处理", GameSaveManager.Verification.RestoreRecoveryVerification.VerifyMultiRootJournalRecoveryAsync);
await RunAsync("安全重试只重试 GET 请求", RetryAndLoggingVerification.VerifySafeRetryHandlerAsync);
await RunAsync("结构化日志会脱敏凭据", RetryAndLoggingVerification.VerifyJsonFileLoggerAsync);
Run("Ludusavi 商店条件与受限 Glob 会返回实际目录", GameSaveManager.Verification.LudusaviManifestVerification.VerifyStoreConditionsAndGlobExpansion);
Run("Ludusavi 安装目录、Alias 循环与二级 Manifest 语义正确", GameSaveManager.Verification.LudusaviManifestVerification.VerifyInstallDirectoryAliasCycleAndSecondaryManifest);
Run("网络错误会转换为可操作的统一错误", GameSaveManager.Verification.ClientOperationErrorVerification.VerifyClassification);
await RunAsync("游戏进程检测会等待并确认稳定进程", GameSaveManager.Verification.GameProcessDetectionVerification.VerifyDelayedPollingAndStableProcessAsync);
await RunAsync("游戏启动不会保存或确认无关系统进程", GameSaveManager.Verification.GameLaunchSafetyVerification.VerifySystemProcessIsNeverPersistedOrConfirmedAsync);
Run("CMS 无时区日期与时间戳可以兼容解析", CmsDateTimeOffsetVerification.Verify);
Run("客户端与服务端协议限制和 JSON 样例保持一致", GameSaveManager.Verification.ProtocolContractVerification.Verify);
Run("新增游戏来源切换不会复用上一款游戏配置", GameSaveManager.Verification.AddGameWizardStateVerification.VerifySelectionIsolationAndSaveNavigation);
await RunAsync("添加向导逐步门禁与多根聚合预览有效", GameSaveManager.Verification.AddGameWizardValidationVerification.VerifyStepGatesAndAggregatePreviewAsync);
Run("游戏详情同步摘要与进度按游戏隔离", GameSaveManager.Verification.GameDetailSyncStateVerification.VerifyPerGameSyncStateIsolation);
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
        await store.SaveAsync(new LocalGameProfile("server-a", "same-game", "LOCAL", null, "D:\\Games\\A", "D:\\Saves\\A", "game-a.exe", "D:\\Games\\A\\game-a.exe", SaveLocationSource.Manual, 100, true, true, [SaveRootRule.CreateDefault("D:\\Saves\\A", SaveLocationSource.Manual, 100, true), new SaveRootRule("root2", "D:\\Saves\\A2", ["**/*.sav"], ["**/cache/**"], SaveLocationSource.Manual, 100, true)]), CancellationToken.None);
        await store.SaveAsync(new LocalGameProfile("server-b", "same-game", "STEAM", "123", "E:\\Games\\B", "E:\\Saves\\B", "game-b.exe", null, SaveLocationSource.StoreMetadata, 90, true, false), CancellationToken.None);
        LocalGameProfile? serverA = await store.GetAsync("server-a", "same-game", CancellationToken.None);
        LocalGameProfile? serverB = await store.GetAsync("server-b", "same-game", CancellationToken.None);
        Check(serverA is { Provider: "LOCAL", SaveDirectory: "D:\\Saves\\A", ProcessName: "game-a.exe", ExecutablePath: "D:\\Games\\A\\game-a.exe", UserConfirmed: true, AutoSnapshotEnabled: true } && serverA.EffectiveSaveRoots.Count == 2 && serverA.EffectiveSaveRoots[1].RootId == "root2", "server-a local profile read failed");
        Check(serverB is { Provider: "STEAM", ProviderGameId: "123", SaveDirectory: "E:\\Saves\\B", ProcessName: "game-b.exe", ExecutablePath: null, UserConfirmed: true, AutoSnapshotEnabled: false }, "server-b local profile read failed");
    }
    finally
    {
        TryDelete(databasePath);
        TryDelete(databasePath + "-wal");
        TryDelete(databasePath + "-shm");
        try { Directory.Delete(tempDirectory, recursive: true); } catch (IOException) { }
    }
}
async Task VerifyLocalGameProfileSchemaResetAsync()
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
async Task VerifyGlobAndRegistryManifestAsync()
{
    string root = Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
    string registry = Path.Combine(root, "registry");
    Directory.CreateDirectory(Path.Combine(registry, "nested"));
    await File.WriteAllTextAsync(Path.Combine(registry, "registry1.json"), "{}");
    await File.WriteAllTextAsync(Path.Combine(registry, "nested", "registry2.json"), "{}");
    string db = Path.Combine(root, "hashes.db");
    try
    {
        var scanner = new SaveDirectoryScanner();
        SaveRootRule topOnly = new("registry", registry, ["*.json"], [], SaveLocationSource.Manual, 100, true);
        SaveRootRule recursive = new("registry", registry, ["**/*.json"], [], SaveLocationSource.Manual, 100, true);
        Check((await scanner.ScanAsync(topOnly, CancellationToken.None)).Count == 1, "*.json 应只匹配根目录 JSON");
        Check((await scanner.ScanAsync(recursive, CancellationToken.None)).Count == 2, "**/*.json 应匹配根目录与子目录 JSON");
        var database = new SqliteDatabase(db);
        await database.InitializeAsync(CancellationToken.None);
        var builder = new SaveManifestBuilder(scanner, new FileHashService(), new SqliteFileHashCache(database));
        IReadOnlyList<GameSaveManager.Domain.Snapshots.SnapshotFile> manifest = await builder.BuildAsync([new SaveRootRule("registry", registry, ["*.json", "**/*.json"], [], SaveLocationSource.Manual, 100, true)], CancellationToken.None);
        Check(manifest.Any(file => file.RelativePath == "registry/registry1.json"), "注册表根目录 JSON 未进入 Manifest");
        Check(manifest.Any(file => file.RelativePath == "registry/nested/registry2.json"), "注册表子目录 JSON 未进入 Manifest");
    }
    finally { try { Directory.Delete(root, recursive: true); } catch (IOException) { } }
}
async Task VerifyRegistrySnapshotAsync()
{
    string ruleId = "verification-" + Guid.NewGuid().ToString("N");
    string relativeKey = "Software\\GameSaveManager\\Verification\\" + ruleId;
    string keyPath = "HKCU\\" + relativeKey;
    string directory = Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", ruleId);
    try
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(relativeKey, writable: true)!) key.SetValue("value", "before");
        var service = new WindowsRegistrySaveSnapshotService();
        var rules = new[] { new RegistrySaveRule(ruleId, keyPath, true) };
        await service.ExportAsync(directory, rules, CancellationToken.None);
        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(relativeKey, writable: true)!) key.SetValue("value", "after");
        await service.ImportAsync(directory, rules, CancellationToken.None);
        using RegistryKey restored = Registry.CurrentUser.OpenSubKey(relativeKey, writable: false) ?? throw new InvalidOperationException("注册表键未恢复");
        Check(string.Equals(restored.GetValue("value")?.ToString(), "before", StringComparison.Ordinal), "注册表值未恢复为快照内容");        using (RegistryKey key = Registry.CurrentUser.OpenSubKey(relativeKey, writable: true)!) key.SetValue("value", "safety");
        string transactionDirectory = Path.Combine(directory, "transaction");
        var transaction = (IRegistryRestoreTransaction)service;
        RegistryRestorePreparation preparation = await transaction.PrepareAsync(directory, rules, transactionDirectory, CancellationToken.None);
        await transaction.ApplyAsync(preparation, CancellationToken.None);
        using (RegistryKey applied = Registry.CurrentUser.OpenSubKey(relativeKey, writable: false)!)
            Check(string.Equals(applied.GetValue("value")?.ToString(), "before", StringComparison.Ordinal), "注册表事务未应用快照内容");
        await transaction.RollbackAsync(preparation, CancellationToken.None);
        using (RegistryKey rolledBack = Registry.CurrentUser.OpenSubKey(relativeKey, writable: false)!)
            Check(string.Equals(rolledBack.GetValue("value")?.ToString(), "safety", StringComparison.Ordinal), "注册表事务未恢复安全备份");
    }
    finally
    {
        Registry.CurrentUser.DeleteSubKeyTree(relativeKey, throwOnMissingSubKey: false);
        try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        string safetyParent = Path.GetDirectoryName(directory)!;
        foreach (string path in Directory.Exists(safetyParent) ? Directory.EnumerateDirectories(safetyParent, "registry-safety-*") : [])
        {
            try { Directory.Delete(path, recursive: true); } catch (IOException) { }
        }
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
