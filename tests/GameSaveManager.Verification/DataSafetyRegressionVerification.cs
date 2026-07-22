using System.Security.Cryptography;
using System.Reflection;
using System.Collections;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Device;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Restores;
using GameSaveManager.Application.Security;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Sync;
using GameSaveManager.Domain.Snapshots;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.Verification;

internal static class DataSafetyRegressionVerification
{
    public static async Task VerifyDestructiveSyncAndAmbiguousCommitAsync()
    {
        string root = CreateTemporaryDirectory("sync-safety");
        try
        {
            string saveDirectory = Path.Combine(root, "save");
            Directory.CreateDirectory(saveDirectory);
            string currentPath = Path.Combine(saveDirectory, "current.sav");
            await File.WriteAllTextAsync(currentPath, "current");
            var hashService = new FileHashService();
            string currentHash = await hashService.ComputeSha256Async(currentPath, CancellationToken.None);
            long currentSize = new FileInfo(currentPath).Length;
            const string gameId = "game-destructive";
            const string userId = "user-a";
            Uri server = new("https://example.test/");
            string serverKey = GameSaveServerIdentity.CreateStableKey(server);
            var api = new RecordingApiClient
            {
                Head = new CloudHead(gameId, "head-1", 1),
                NextSnapshotId = "head-2"
            };
            api.Snapshots["head-1"] = Manifest(
                gameId,
                "head-1",
                [Root("root")],
                [
                    CloudFile("root/current.sav", currentHash, currentSize),
                    CloudFile("root/removed-1.sav", Hex('1'), 1),
                    CloudFile("root/removed-2.sav", Hex('2'), 1),
                    CloudFile("root/removed-3.sav", Hex('3'), 1)
                ]);
            var stateStore = new MemorySyncStateStore(new LocalSyncState(
                serverKey, gameId, "head-1", 1, userId));
            CloudSyncService service = CreateSyncService(api, stateStore, hashService);
            SaveRootRule saveRoot = SaveRootRule.CreateDefault(
                saveDirectory, SaveLocationSource.Manual, 100, true);

            var wrongHeadApi = new RecordingApiClient
            {
                Head = new CloudHead("another-game", null, 0)
            };
            await ExpectThrowsAsync<InvalidDataException>(() => CreateSyncService(
                    wrongHeadApi, new MemorySyncStateStore(), hashService)
                .CheckFreshnessAsync(server, "token", userId, gameId, [saveRoot], CancellationToken.None));
            Ensure(wrongHeadApi.CommitCalls == 0,
                "服务端返回其他游戏的 HEAD 时，客户端必须在任何提交前拒绝结果。" );

            var duplicateMissingApi = new RecordingApiClient
            {
                Head = new CloudHead(gameId, null, 0),
                MissingResponse =
                [
                    new ContentObjectDescriptor(currentHash, currentSize),
                    new ContentObjectDescriptor(currentHash.ToUpperInvariant(), currentSize)
                ]
            };
            await ExpectThrowsAsync<InvalidDataException>(() => CreateSyncService(
                    duplicateMissingApi, new MemorySyncStateStore(), hashService)
                .SyncAsync(server, "token", userId, gameId, [saveRoot], SnapshotTrigger.Manual,
                    null, CancellationToken.None));
            Ensure(duplicateMissingApi.CommitCalls == 0,
                "服务端返回重复或越界缺失对象时，客户端不得继续上传或提交。" );

            CloudFreshnessResult freshness = await service.CheckFreshnessAsync(
                server, "token", userId, gameId, [saveRoot], CancellationToken.None);
            Ensure(freshness.Status == CloudFreshnessStatus.LocalDataMissing,
                "本机文件大量消失但 HEAD 未变化时，必须识别为本地数据缺失，不能显示已是最新。" );

            var repairedState = new MemorySyncStateStore();
            var matchingApi = new RecordingApiClient
            {
                Head = new CloudHead(gameId, "matching-head", 3)
            };
            matchingApi.Snapshots["matching-head"] = Manifest(
                gameId, "matching-head", [Root("root")],
                [CloudFile("root/current.sav", currentHash, currentSize)]);
            CloudFreshnessResult repaired = await CreateSyncService(
                    matchingApi, repairedState, hashService)
                .CheckFreshnessAsync(server, "token", userId, gameId,
                    [saveRoot], CancellationToken.None);
            Ensure(repaired.Status == CloudFreshnessStatus.UpToDate
                   && repairedState.State?.HeadSnapshotId == "matching-head",
                "客户端本地状态丢失但文件与云端完全一致时，应自动修复同步基线而不是制造伪冲突。" );

            await ExpectThrowsAsync<DestructiveSnapshotChangeException>(() => service.SyncAsync(
                server, "token", userId, gameId, [saveRoot], SnapshotTrigger.Manual,
                null, CancellationToken.None));
            Ensure(api.CommitCalls == 0,
                "破坏性文件减少未经明确确认时，不得调用云端快照提交。" );

            CloudSyncResult forced = await service.SyncAsync(
                server, "token", userId, gameId, [saveRoot], SnapshotTrigger.Manual,
                null, CancellationToken.None, allowDestructiveChanges: true);
            Ensure(forced.Status == CloudSyncStatus.Success && forced.RemovedFileCount == 3,
                "用户明确确认后应允许提交，并准确记录删除文件数量。" );
            Ensure(stateStore.State is { HeadSnapshotId: "head-2", UserId: userId },
                "成功提交后必须把新 HEAD 写入正确账号的本地同步状态。" );

            string ambiguousDirectory = Path.Combine(root, "ambiguous");
            Directory.CreateDirectory(ambiguousDirectory);
            string ambiguousPath = Path.Combine(ambiguousDirectory, "slot.sav");
            await File.WriteAllTextAsync(ambiguousPath, "new-content");
            string ambiguousHash = await hashService.ComputeSha256Async(ambiguousPath, CancellationToken.None);
            long ambiguousSize = new FileInfo(ambiguousPath).Length;
            var ambiguousApi = new RecordingApiClient
            {
                Head = new CloudHead("game-ambiguous", "old-head", 7),
                NextSnapshotId = "persisted-head",
                FailCommitAfterPersisting = true
            };
            ambiguousApi.Snapshots["old-head"] = Manifest(
                "game-ambiguous", "old-head", [Root("root")],
                [CloudFile("root/slot.sav", Hex('a'), ambiguousSize)]);
            var ambiguousState = new MemorySyncStateStore(new LocalSyncState(
                serverKey, "game-ambiguous", "old-head", 7, userId));
            CloudSyncResult reconciled = await CreateSyncService(
                    ambiguousApi, ambiguousState, hashService)
                .SyncAsync(server, "token", userId, "game-ambiguous",
                    [SaveRootRule.CreateDefault(ambiguousDirectory, SaveLocationSource.Manual, 100, true)],
                    SnapshotTrigger.Manual, null, CancellationToken.None);
            Ensure(reconciled.Status == CloudSyncStatus.Success
                   && reconciled.SnapshotId == "persisted-head"
                   && ambiguousState.State?.HeadSnapshotId == "persisted-head",
                "提交响应丢失但服务端已成功时，客户端必须核对远端 Manifest 并修复本地 HEAD。" );
            Ensure(ambiguousApi.CommitCalls == 1
                   && ambiguousApi.Snapshots["persisted-head"].Files.Single().Sha256 == ambiguousHash,
                "提交歧义核对必须使用服务端实际落盘的新快照。" );

            var corruptedMetadataApi = new RecordingApiClient
            {
                Head = new CloudHead("game-metadata", null, 0),
                NextSnapshotId = "metadata-head",
                CorruptCommittedRootMetadata = true
            };
            var corruptedMetadataState = new MemorySyncStateStore();
            await ExpectThrowsAsync<InvalidDataException>(() => CreateSyncService(
                    corruptedMetadataApi, corruptedMetadataState, hashService)
                .SyncAsync(server, "token", userId, "game-metadata",
                    [SaveRootRule.CreateDefault(ambiguousDirectory, SaveLocationSource.Manual, 100, true)],
                    SnapshotTrigger.Manual, null, CancellationToken.None));
            Ensure(corruptedMetadataState.State is null,
                "服务端落盘的存档路径元数据与请求不一致时，不得把该快照确认为本地同步基线。" );
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    public static async Task VerifyExactHistoricalRestoreAsync()
    {
        string root = CreateTemporaryDirectory("restore-safety");
        try
        {
            byte[] cloudContent = "cloud-save"u8.ToArray();
            string hash = Convert.ToHexString(SHA256.HashData(cloudContent)).ToLowerInvariant();
            const string gameId = "game-restore";
            const string userId = "user-restore";
            Uri server = new("https://example.test/");
            string serverKey = GameSaveServerIdentity.CreateStableKey(server);
            var api = new RecordingApiClient
            {
                Head = new CloudHead(gameId, "newer-head", 9)
            };
            api.Objects[hash] = cloudContent;
            api.Snapshots["historical-head"] = Manifest(
                gameId,
                "historical-head",
                [Root("root1"), Root("root2")],
                [CloudFile("root1/cloud.sav", hash, cloudContent.Length)]);
            api.Snapshots["newer-head"] = Manifest(
                gameId,
                "newer-head",
                [Root("root1"), Root("root2")],
                [CloudFile("root1/newer.sav", Hex('b'), 1)]);

            string missingRoot = Path.Combine(root, "missing-root1");
            Directory.CreateDirectory(missingRoot);
            await File.WriteAllTextAsync(Path.Combine(missingRoot, "untouched.sav"), "untouched");
            var missingStore = new MemorySyncStateStore();
            SafeRestoreService missingService = CreateRestoreService(api, missingStore, root);
            await ExpectThrowsAsync<InvalidDataException>(() => missingService.RestoreAsync(
                server, "token", userId, gameId, "historical-head",
                [ConfirmedRoot("root1", missingRoot)], [], CancellationToken.None));
            Ensure(File.Exists(Path.Combine(missingRoot, "untouched.sav")) && api.DownloadCalls == 0,
                "缺少快照根目录映射时必须在下载或移动任何本地数据前失败。" );

            string first = Path.Combine(root, "root1");
            string second = Path.Combine(root, "root2");
            string laterAdded = Path.Combine(root, "root3");
            Directory.CreateDirectory(first);
            Directory.CreateDirectory(second);
            Directory.CreateDirectory(laterAdded);
            await File.WriteAllTextAsync(Path.Combine(first, "old-1.sav"), "old-1");
            await File.WriteAllTextAsync(Path.Combine(second, "old-2.sav"), "old-2");
            await File.WriteAllTextAsync(Path.Combine(laterAdded, "must-stay.sav"), "must-stay");
            var stateStore = new MemorySyncStateStore(new LocalSyncState(
                serverKey, gameId, "newer-head", 9, userId));
            SafeRestoreService service = CreateRestoreService(api, stateStore, root);

            await ExpectThrowsAsync<InvalidOperationException>(() => service.RestoreAsync(
                server, "token", userId, gameId, "historical-head",
                [
                    ConfirmedRoot("root1", first),
                    ConfirmedRoot("root2", second),
                    ConfirmedRoot("root3", laterAdded)
                ], [], CancellationToken.None,
                () => throw new InvalidOperationException("模拟下载期间游戏启动")));
            Ensure(await File.ReadAllTextAsync(Path.Combine(first, "old-1.sav")) == "old-1"
                   && await File.ReadAllTextAsync(Path.Combine(second, "old-2.sav")) == "old-2"
                   && stateStore.State is { HeadSnapshotId: "newer-head", HeadVersion: 9 },
                "正式替换前的最后校验失败时，原存档与原同步基线必须完全保持不变。" );

            IReadOnlyList<RestoreResult> results = await service.RestoreAsync(
                server, "token", userId, gameId, "historical-head",
                [
                    ConfirmedRoot("root1", first),
                    ConfirmedRoot("root2", second),
                    ConfirmedRoot("root3", laterAdded)
                ], [], CancellationToken.None);

            Ensure(results.Count == 2,
                "历史快照只应恢复自身声明的根目录，不得清空后来新增的本地根目录。" );
            Ensure(File.ReadAllBytes(Path.Combine(first, "cloud.sav")).SequenceEqual(cloudContent)
                   && !File.Exists(Path.Combine(first, "old-1.sav")),
                "非空快照根目录必须精确替换为云端内容。" );
            Ensure(Directory.Exists(second) && !Directory.EnumerateFileSystemEntries(second).Any(),
                "快照中明确声明但为空的根目录也必须被精确恢复为空目录。" );
            Ensure(await File.ReadAllTextAsync(Path.Combine(laterAdded, "must-stay.sav")) == "must-stay",
                "快照未声明的后来新增根目录不得被恢复流程修改。" );
            Ensure(results.All(result => result.SafetyBackupDirectory is not null
                                         && Directory.Exists(result.SafetyBackupDirectory)),
                "覆盖前的每个原存档根目录都必须保留可恢复的安全备份。" );
            Ensure(stateStore.State is
                   {
                       HeadSnapshotId: "historical-head",
                       HeadVersion: LocalSyncState.IntentionalRestorePendingVersion,
                       UserId: userId
                   },
                "恢复历史快照后，本地基线必须记录实际恢复的历史快照，而不是当前远端 HEAD。" );
            CloudFreshnessResult restoredFreshness = await CreateSyncService(
                    api, stateStore, new FileHashService())
                .CheckFreshnessAsync(
                    server,
                    "token",
                    userId,
                    gameId,
                    [
                        ConfirmedRoot("root1", first),
                        ConfirmedRoot("root2", second),
                        ConfirmedRoot("root3", laterAdded)
                    ],
                    CancellationToken.None);
            Ensure(restoredFreshness.Status == CloudFreshnessStatus.Diverged,
                "历史恢复的版本选择必须跨客户端重启保持，启动前不得自动用当前云端 HEAD 覆盖。" );
            Ensure(api.DownloadCalls == 1,
                "恢复对象应通过校验缓存下载一次，空根目录不得产生伪下载。" );
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    public static async Task VerifyHashCacheCannotOverrideFileContentAsync()
    {
        string root = CreateTemporaryDirectory("hash-cache-fact");
        try
        {
            string file = Path.Combine(root, "slot.sav");
            byte[] content = "new-content"u8.ToArray();
            await File.WriteAllBytesAsync(file, content);
            string expectedHash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
            var poisonedCache = new PoisonedHashCache(Hex('0'));
            var builder = new SaveManifestBuilder(
                new SaveDirectoryScanner(),
                new FileHashService(),
                poisonedCache);

            IReadOnlyList<SnapshotFile> manifest = await builder.BuildAsync(
                [ConfirmedRoot("root", root)], CancellationToken.None);

            Ensure(manifest.Single().Sha256 == expectedHash
                   && poisonedCache.LastStoredHash == expectedHash,
                "正式 Manifest 必须以当前文件真实内容为准，不能复用同大小同时间戳的旧 Hash。" );
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    public static async Task VerifyStablePhysicalDeviceIdentityAsync()
    {
        var credentials = new MemoryCredentialStore();
        var machine = new FixedDeviceIdentityProvider("win-machine-stable-001");
        string first = await new CredentialDeviceIdentityProvider(
                credentials,
                new FixedDeviceIdentityProvider("legacy-random-001"),
                machine)
            .GetOrCreateDeviceIdAsync(CancellationToken.None);

        credentials.Clear();
        string afterLocalDataDeletion = await new CredentialDeviceIdentityProvider(
                credentials,
                new FixedDeviceIdentityProvider("legacy-random-999"),
                machine)
            .GetOrCreateDeviceIdAsync(CancellationToken.None);
        Ensure(first == afterLocalDataDeletion && first == "win-machine-stable-001",
            "本地数据库和凭据都丢失后，同一台电脑仍应从安装级身份派生同一个设备 ID。" );

        await credentials.SaveAsync(
            CredentialTargets.StableDeviceId, "existing-device-001", CancellationToken.None);
        string existing = await new CredentialDeviceIdentityProvider(
                credentials,
                new FixedDeviceIdentityProvider("legacy-random-002"),
                new FixedDeviceIdentityProvider("win-machine-different"))
            .GetOrCreateDeviceIdAsync(CancellationToken.None);
        Ensure(existing == "existing-device-001",
            "升级已有客户端时必须优先沿用已注册设备 ID，避免升级本身制造重复设备记录。" );
    }

    public static async Task VerifyLateOldSessionWriteIsRejectedAsync()
    {
        string root = CreateTemporaryDirectory("session-isolation");
        try
        {
            string saveDirectory = Path.Combine(root, "save");
            Directory.CreateDirectory(saveDirectory);
            await File.WriteAllTextAsync(Path.Combine(saveDirectory, "slot.sav"), "data");
            var store = new BlockingProfileStore();
            MainViewModel viewModel = SmokeViewModelFactory.Create(store);
            const string gameId = "same-id-on-two-accounts";
            viewModel.SelectedGame = new CloudGame(gameId, "会话隔离游戏", "CUSTOM", null);
            viewModel.SaveDirectory = saveDirectory;
            typeof(MainViewModel).GetField("_isSaveDirectoryConfirmed",
                    BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(viewModel, true);

            MethodInfo saveMethod = typeof(MainViewModel).GetMethod(
                "SaveLocalProfileAsync", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("未找到本机配置保存方法。" );
            Task pendingSave = (Task)(saveMethod.Invoke(viewModel,
                [new Uri("http://localhost:8080"), false, null, gameId, 0L])
                ?? throw new InvalidOperationException("本机配置保存未返回任务。" ));
            await store.SaveStarted.Task;

            MethodInfo beginTransition = typeof(MainViewModel).GetMethod(
                "BeginSessionTransition", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("未找到会话切换方法。" );
            beginTransition.Invoke(viewModel, null);
            store.AllowSaveToFinish.TrySetResult();
            await ExpectThrowsAsync<InvalidOperationException>(() => pendingSave);

            var profiles = (Dictionary<string, LocalGameProfile>)(typeof(MainViewModel)
                .GetField("_localGameProfiles", BindingFlags.Instance | BindingFlags.NonPublic)!
                .GetValue(viewModel)
                ?? throw new InvalidOperationException("无法读取本机配置缓存。" ));
            Ensure(!profiles.ContainsKey(gameId),
                "旧会话延迟完成的本机写入不得进入新会话的内存缓存，即使两个账号的游戏 ID 相同。" );
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    public static void VerifyRuntimeLearningKeepsSpecificSaveDirectory()
    {
        string root = CreateTemporaryDirectory("runtime-learning-specificity");
        try
        {
            string saveDirectory = Path.Combine(root, "Azrael_swarm");
            Directory.CreateDirectory(saveDirectory);
            IReadOnlyList<FileMetadataSnapshot> changed =
            [
                new(Path.Combine(root, "launcher-state.bin"), 1, DateTime.UtcNow),
                new(Path.Combine(saveDirectory, "slot.dat"), 1, DateTime.UtcNow)
            ];
            Type serviceType = typeof(GameSaveManager.Infrastructure.Discovery.WindowsRuntimeSaveLearningService);
            MethodInfo build = serviceType.GetMethod(
                "BuildCandidateDirectories", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("未找到运行学习候选合并方法。" );
            var directories = new List<string>();
            foreach (object item in (IEnumerable)(build.Invoke(null, [changed])
                                                  ?? throw new InvalidOperationException("运行学习未返回候选。" )))
            {
                directories.Add((string)(item.GetType().GetField("Item1")?.GetValue(item)
                                         ?? throw new InvalidOperationException("无法读取候选目录。" )));
            }
            Ensure(directories.Any(path => string.Equals(path, saveDirectory, StringComparison.OrdinalIgnoreCase)),
                "游戏根目录和新存档子目录同时变化时，运行学习不得把具体存档目录折叠掉。" );

            MethodInfo score = serviceType.GetMethod(
                "Score", BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("未找到运行学习评分方法。" );
            int rootScore = (int)(score.Invoke(null, [root, root]) ?? -1);
            int saveScore = (int)(score.Invoke(null, [saveDirectory, root]) ?? -1);
            Ensure(saveScore > rootScore,
                "运行学习必须把具体变化子目录排在游戏安装根目录之前。" );
        }
        finally
        {
            DeleteDirectory(root);
        }
    }

    private static CloudSyncService CreateSyncService(
        IGameSaveApiClient api,
        ILocalSyncStateStore stateStore,
        IFileHashService hashService) =>
        new(
            new SaveManifestBuilder(new SaveDirectoryScanner(), hashService, new NullHashCache()),
            api,
            stateStore,
            new IdentityPathTemplateService());

    private static SafeRestoreService CreateRestoreService(
        IGameSaveApiClient api,
        ILocalSyncStateStore stateStore,
        string root)
    {
        var hashService = new FileHashService();
        return new SafeRestoreService(
            api,
            new ContentObjectCache(hashService, Path.Combine(root, "object-cache")),
            hashService,
            stateStore);
    }

    private static SaveRootRule ConfirmedRoot(string id, string path) =>
        new(id, path, [], [], SaveLocationSource.Manual, 100, true);

    private static CloudSnapshotRoot Root(string id) =>
        new(id, "FILE", "%TEST%", "Manual", 100, [], []);

    private static CloudSnapshotFile CloudFile(string path, string hash, long size) =>
        new(path, hash, hash, size);

    private static CloudSnapshotManifest Manifest(
        string gameId,
        string snapshotId,
        IReadOnlyList<CloudSnapshotRoot> roots,
        IReadOnlyList<CloudSnapshotFile> files) =>
        new(snapshotId, gameId, "device", null, "MANUAL", null,
            DateTimeOffset.UtcNow, roots, files);

    private static string Hex(char value) => new(value, 64);

    private static string CreateTemporaryDirectory(string name)
    {
        string path = Path.Combine(
            Path.GetTempPath(), "GameSaveManager.Verification", name, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。" );
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class NullHashCache : IFileHashCache
    {
        public Task<string?> TryGetAsync(string fullPath, long size, DateTime lastWriteTimeUtc,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);

        public Task UpsertAsync(string fullPath, long size, DateTime lastWriteTimeUtc,
            string sha256, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class PoisonedHashCache(string poisonedHash) : IFileHashCache
    {
        public string? LastStoredHash { get; private set; }

        public Task<string?> TryGetAsync(string fullPath, long size, DateTime lastWriteTimeUtc,
            CancellationToken cancellationToken) => Task.FromResult<string?>(poisonedHash);

        public Task UpsertAsync(string fullPath, long size, DateTime lastWriteTimeUtc,
            string sha256, CancellationToken cancellationToken)
        {
            LastStoredHash = sha256;
            return Task.CompletedTask;
        }
    }

    private sealed class IdentityPathTemplateService : ISavePathTemplateService
    {
        public string Encode(string path) => path;
        public string? Resolve(string pathTemplate) => pathTemplate;
    }

    private sealed class MemorySyncStateStore(LocalSyncState? initial = null) : ILocalSyncStateStore
    {
        public LocalSyncState? State { get; private set; } = initial;

        public Task<LocalSyncState?> GetAsync(string serverKey, string userId, string gameId,
            CancellationToken cancellationToken) =>
            Task.FromResult(State is not null
                            && State.ServerKey == serverKey
                            && State.UserId == userId
                            && State.GameId == gameId
                ? State
                : null);

        public Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken)
        {
            State = state;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(string serverKey, string userId, string gameId,
            CancellationToken cancellationToken)
        {
            if (State is not null
                && State.ServerKey == serverKey
                && State.UserId == userId
                && State.GameId == gameId)
                State = null;
            return Task.CompletedTask;
        }
    }

    private sealed class MemoryCredentialStore : ICredentialStore
    {
        private readonly Dictionary<string, string> _values = new(StringComparer.Ordinal);

        public Task SaveAsync(string target, string secret, CancellationToken cancellationToken)
        {
            _values[target] = secret;
            return Task.CompletedTask;
        }

        public Task<string?> ReadAsync(string target, CancellationToken cancellationToken) =>
            Task.FromResult(_values.TryGetValue(target, out string? value) ? value : null);

        public Task DeleteAsync(string target, CancellationToken cancellationToken)
        {
            _values.Remove(target);
            return Task.CompletedTask;
        }

        public void Clear() => _values.Clear();
    }

    private sealed class FixedDeviceIdentityProvider(string value) : IDeviceIdentityProvider
    {
        public Task<string> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken) =>
            Task.FromResult(value);
    }

    private sealed class BlockingProfileStore : ILocalGameProfileStore
    {
        public TaskCompletionSource SaveStarted { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllowSaveToFinish { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<LocalGameProfile?> GetAsync(string serverKey, string userId, string gameId,
            CancellationToken cancellationToken) => Task.FromResult<LocalGameProfile?>(null);

        public async Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken)
        {
            SaveStarted.TrySetResult();
            await AllowSaveToFinish.Task;
        }

        public Task<IReadOnlyList<LocalGameProfile>> ListAsync(string serverKey, string userId,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LocalGameProfile>>([]);

        public Task ClaimLegacyAsync(string serverKey, string userId,
            IReadOnlyCollection<string> ownedGameIds, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task DeleteAsync(string serverKey, string userId, string gameId,
            CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class RecordingApiClient : IGameSaveApiClient
    {
        public CloudHead Head { get; set; } = new("game", null, 0);
        public string NextSnapshotId { get; set; } = "next-head";
        public bool FailCommitAfterPersisting { get; set; }
        public bool CorruptCommittedRootMetadata { get; set; }
        public IReadOnlyList<ContentObjectDescriptor>? MissingResponse { get; set; }
        public int CommitCalls { get; private set; }
        public int DownloadCalls { get; private set; }
        public Dictionary<string, CloudSnapshotManifest> Snapshots { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, byte[]> Objects { get; } = new(StringComparer.OrdinalIgnoreCase);

        public Task<CloudHead> GetHeadAsync(Uri server, string deviceToken, string gameId,
            CancellationToken cancellationToken) => Task.FromResult(Head);

        public Task<CloudSnapshotManifest> GetSnapshotAsync(Uri server, string deviceToken,
            string gameId, string snapshotId, CancellationToken cancellationToken) =>
            Task.FromResult(Snapshots.TryGetValue(snapshotId, out CloudSnapshotManifest? manifest)
                ? manifest
                : throw new InvalidOperationException($"测试快照不存在：{snapshotId}"));

        public Task<IReadOnlyList<ContentObjectDescriptor>> CheckMissingAsync(Uri server,
            string deviceToken, IReadOnlyCollection<ContentObjectDescriptor> objects,
            CancellationToken cancellationToken) =>
            Task.FromResult(MissingResponse ?? (IReadOnlyList<ContentObjectDescriptor>)[]);

        public Task UploadObjectAsync(Uri server, string deviceToken, string filePath,
            ContentObjectDescriptor descriptor, CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<CloudSnapshotResult> CommitSnapshotAsync(Uri server, string deviceToken,
            string gameId, string? expectedHeadSnapshotId, SnapshotTrigger trigger, string? description,
            IReadOnlyList<SnapshotRootDescriptor> roots, IReadOnlyList<SnapshotFile> files,
            CancellationToken cancellationToken)
        {
            CommitCalls++;
            long nextVersion = Head.Version + 1;
            CloudSnapshotRoot[] committedRoots = roots.Select(root => new CloudSnapshotRoot(
                    root.RootId, root.RootType, root.PathTemplate, root.Source,
                    root.Confidence, root.IncludePatterns, root.ExcludePatterns))
                .ToArray();
            if (CorruptCommittedRootMetadata && committedRoots.Length > 0)
                committedRoots[0] = committedRoots[0] with { PathTemplate = "%DOCUMENTS%\\WrongGame" };
            Snapshots[NextSnapshotId] = new CloudSnapshotManifest(
                NextSnapshotId,
                gameId,
                "device",
                Head.HeadSnapshotId,
                SnapshotTriggerNames.ToApiValue(trigger),
                description,
                DateTimeOffset.UtcNow,
                committedRoots,
                files.Select(file => new CloudSnapshotFile(
                    file.RelativePath, file.Sha256, file.Sha256, file.Size)).ToArray());
            Head = new CloudHead(gameId, NextSnapshotId, nextVersion);
            if (FailCommitAfterPersisting)
                throw new IOException("模拟服务端已提交但响应丢失。" );
            return Task.FromResult(new CloudSnapshotResult(
                NextSnapshotId,
                nextVersion,
                files.Count,
                files.Sum(file => file.Size),
                files.Count,
                true));
        }

        public async Task DownloadObjectAsync(Uri server, string deviceToken, string objectId,
            string destinationPath, long expectedSize, CancellationToken cancellationToken)
        {
            DownloadCalls++;
            if (!Objects.TryGetValue(objectId, out byte[]? content))
                throw new InvalidOperationException($"测试对象不存在：{objectId}" );
            if (content.LongLength != expectedSize)
                throw new InvalidDataException("测试对象大小与请求不一致。" );
            Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
            await File.WriteAllBytesAsync(destinationPath, content, cancellationToken);
        }

        public Task<AuthSession> RegisterAsync(Uri server, string username, string password,
            string deviceId, string deviceName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<AuthSession> LoginAsync(Uri server, string username, string password,
            string deviceId, string deviceName, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CloudAccountSession> GetSessionAsync(Uri server, string deviceToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CloudRetentionPolicy> GetRetentionPolicyAsync(Uri server, string deviceToken,
            string gameId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CloudRetentionPolicy> UpdateRetentionPolicyAsync(Uri server, string deviceToken,
            string gameId, bool enabled, int retentionCount, int retentionDays,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CloudRetentionCleanupResult> CleanupRetentionAsync(Uri server,
            string deviceToken, string gameId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task<CloudQuota> GetQuotaAsync(Uri server, string deviceToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CloudDevice>> ListDevicesAsync(Uri server, string deviceToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task RevokeDeviceAsync(Uri server, string deviceToken, string deviceId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CloudGame>> ListGamesAsync(Uri server, string deviceToken,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CloudGame> CreateGameAsync(Uri server, string deviceToken, string name,
            string provider, string? providerGameId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteGameAsync(Uri server, string deviceToken, string gameId,
            CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CloudSnapshotSummary>> ListSnapshotsAsync(Uri server,
            string deviceToken, string gameId, int limit, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
        public Task DeleteSnapshotAsync(Uri server, string deviceToken, string gameId,
            string snapshotId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
