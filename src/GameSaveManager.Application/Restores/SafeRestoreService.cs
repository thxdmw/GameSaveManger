using System.Text.Json;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Sync;

namespace GameSaveManager.Application.Restores;

/// <summary>
/// 用于恢复游戏存档的事务编排：对象先校验进缓存，再构建临时目录；
/// 原存档目录仅在临时目录完整校验后才会移动为安全备份，绝不先删除再复制。
/// </summary>
public sealed class SafeRestoreService(
    IGameSaveApiClient apiClient,
    ContentObjectCache objectCache,
    IFileHashService fileHashService,
    ILocalSyncStateStore localSyncStateStore,
    IRegistryRestoreTransaction? registryRestoreTransaction = null)
{
    private static readonly JsonSerializerOptions JournalJsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private static readonly string ApplicationDataRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameSaveManager");
    private static readonly string RestoreRoot = Path.Combine(ApplicationDataRoot, "restore");

    public async Task<RestoreResult> RestoreAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        string saveDirectory,
        CancellationToken cancellationToken,
        Action? validateBeforeApply = null)
    {
        SaveRootTopologyValidator.Validate(
            [SaveRootRule.CreateDefault(saveDirectory, Discovery.SaveLocationSource.Manual, 100, true)]);
        CloudSnapshotManifest manifest = await apiClient.GetSnapshotAsync(server, deviceToken, gameId, snapshotId, cancellationToken);
        ValidateManifest(manifest, gameId, snapshotId);
        if (manifest.Roots is { Count: > 0 })
        {
            CloudSnapshotRoot[] fileRoots = manifest.Roots
                .Where(root => string.Equals(root.RootType, "FILE", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (fileRoots.Length != 1 || manifest.Roots.Any(root => string.Equals(root.RootType, "REGISTRY", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("该快照包含多个存档根目录或注册表数据，必须使用多根目录恢复接口。");
            string prefix = fileRoots[0].RootId + "/";
            if (manifest.Files.Any(file => !file.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("快照路径与根目录元数据不一致。");
            manifest = manifest with
            {
                Files = manifest.Files.Select(file => file with { RelativePath = file.RelativePath[prefix.Length..] }).ToArray()
            };
        }
        LocalSyncState? previousState = await MarkRestorePendingAsync(
            server, string.Empty, gameId, manifest.SnapshotId, cancellationToken);
        try
        {
            return await RestoreOneAsync(
                server, deviceToken, manifest, saveDirectory, cancellationToken,
                validateBeforeApply);
        }
        catch (RestoreRollbackFailedException)
        {
            throw;
        }
        catch
        {
            await TryRestorePreviousStateAsync(server, string.Empty, gameId, previousState);
            throw;
        }
    }

    /// <summary>按存档根标识拆分云端文件，分别恢复到每个已确认的本地目录。</summary>
    public Task<IReadOnlyList<RestoreResult>> RestoreAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        IReadOnlyList<SaveRootRule> roots,
        CancellationToken cancellationToken,
        Action? validateBeforeApply = null) =>
        RestoreAsync(server, deviceToken, string.Empty, gameId, snapshotId, roots, [],
            cancellationToken, validateBeforeApply);

    public async Task<IReadOnlyList<RestoreResult>> RestoreAsync(
        Uri server,
        string deviceToken,
        string userId,
        string gameId,
        string snapshotId,
        IReadOnlyList<SaveRootRule> roots,
        IReadOnlyList<RegistrySaveRule> registryRules,
        CancellationToken cancellationToken,
        Action? validateBeforeApply = null)
    {
        if (roots is null || roots.Count == 0) throw new InvalidOperationException("至少需要一个存档根目录。");
        if (roots.Any(root => !root.UserConfirmed)) throw new InvalidOperationException("所有存档根目录都必须经用户确认。");
        SaveRootRule[] fileRootsForSafety = roots.Where(root =>
            !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fileRootsForSafety.Length > 0) SaveRootTopologyValidator.Validate(fileRootsForSafety);
        foreach (SaveRootRule registryRoot in roots.Where(root =>
                     string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)))
            ValidateGeneratedRegistryRoot(registryRoot.Path);
        if (roots.Select(root => root.RootId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != roots.Count)
            throw new InvalidOperationException("存档根目录标识不能重复。");
        if (registryRules.Any(rule => !rule.UserConfirmed))
            throw new InvalidOperationException("所有注册表存档规则都必须经用户确认。");
        if (registryRules.Select(rule => rule.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != registryRules.Count)
            throw new InvalidOperationException("注册表存档规则标识不能重复。");

        CloudSnapshotManifest manifest = await apiClient.GetSnapshotAsync(server, deviceToken, gameId, snapshotId, cancellationToken);
        ValidateManifest(manifest, gameId, snapshotId);
        SnapshotPathFormat pathFormat = DetectPathFormat(manifest, roots);
        if (pathFormat == SnapshotPathFormat.LegacySingleRoot)
        {
            if (registryRules.Count > 0) throw new InvalidDataException("旧版单根快照不包含注册表数据，拒绝混合恢复。");
            SaveRootRule root = roots.Single(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase));
            LocalSyncState? previousState = await MarkRestorePendingAsync(
                server, userId, gameId, manifest.SnapshotId, cancellationToken);
            try
            {
                RestoreResult result = await RestoreOneAsync(
                    server, deviceToken, manifest, root.Path, cancellationToken,
                    validateBeforeApply);
                return [result];
            }
            catch (RestoreRollbackFailedException)
            {
                throw;
            }
            catch
            {
                await TryRestorePreviousStateAsync(server, userId, gameId, previousState);
                throw;
            }
        }
        LocalSyncState? previousNamespacedState = await MarkRestorePendingAsync(
            server, userId, gameId, manifest.SnapshotId, cancellationToken);
        try
        {
            return await RestoreNamespacedRootsAsync(
                server, deviceToken, gameId, manifest, roots, registryRules,
                cancellationToken, validateBeforeApply);
        }
        catch (RestoreRollbackFailedException)
        {
            throw;
        }
        catch
        {
            await TryRestorePreviousStateAsync(
                server, userId, gameId, previousNamespacedState);
            throw;
        }
    }

    public Task<IReadOnlyList<RestoreResult>> RestoreAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        IReadOnlyList<SaveRootRule> roots,
        IReadOnlyList<RegistrySaveRule> registryRules,
        CancellationToken cancellationToken,
        Action? validateBeforeApply = null) =>
        RestoreAsync(server, deviceToken, string.Empty, gameId, snapshotId, roots,
            registryRules, cancellationToken, validateBeforeApply);

    private async Task<IReadOnlyList<RestoreResult>> RestoreNamespacedRootsAsync(
        Uri server, string deviceToken, string gameId, CloudSnapshotManifest manifest,
        IReadOnlyList<SaveRootRule> roots, IReadOnlyList<RegistrySaveRule> registryRules,
        CancellationToken cancellationToken, Action? validateBeforeApply)
    {
        Dictionary<string, SaveRootRule> configuredRoots = roots.ToDictionary(
            root => root.RootId,
            StringComparer.OrdinalIgnoreCase);
        HashSet<string> declaredRootIds = manifest.Roots is { Count: > 0 }
            ? manifest.Roots.Select(root => root.RootId).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : manifest.Files.Select(file => GetNamespacedRootId(file.RelativePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        string[] missingMappings = declaredRootIds
            .Where(rootId => !configuredRoots.ContainsKey(rootId))
            .ToArray();
        if (missingMappings.Length > 0)
            throw new InvalidDataException($"当前电脑尚未配置快照所需的存档根目录：{string.Join("、", missingMappings)}。请先按云端路径记录完成目录映射。");
        if (registryRules.Count > 0 && !declaredRootIds.Contains("registry"))
            throw new InvalidDataException("该历史快照没有注册表根目录，无法在不猜测删除范围的情况下精确恢复；请先移除注册表恢复规则或导出后人工处理。");
        if (declaredRootIds.Contains("registry") && registryRules.Count == 0)
            throw new InvalidDataException("该快照包含注册表存档，但当前电脑没有对应的已确认注册表规则，已拒绝不完整恢复。");
        SaveRootRule[] restoreRoots = declaredRootIds
            .Select(rootId => configuredRoots[rootId])
            .ToArray();
        ValidateRootDirectories(restoreRoots);
        string transactionId = Guid.NewGuid().ToString("N");
        string transactionDirectory = Path.Combine(RestoreRoot, transactionId);
        EnsureNoReparsePointTraversal(ApplicationDataRoot, transactionDirectory);
        Directory.CreateDirectory(transactionDirectory);
        EnsureNoReparsePointTraversal(ApplicationDataRoot, transactionDirectory);
        var plans = new List<RestoreRootPlan>();
        foreach (SaveRootRule root in restoreRoots)
        {
            string prefix = root.RootId + "/";
            IReadOnlyList<CloudSnapshotFile> files = manifest.Files.Where(file => file.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(file => file with { RelativePath = file.RelativePath[prefix.Length..] }).ToArray();
            string target = Path.GetFullPath(root.Path);
            if (File.Exists(target)) throw new IOException($"存档目录路径被同名文件占用: {target}");
            string parent = Path.GetDirectoryName(target) ?? throw new IOException("存档目录必须具有父目录。");
            Directory.CreateDirectory(parent);
            plans.Add(new RestoreRootPlan(root.RootId, target,
                Path.Combine(parent, $".{Path.GetFileName(target)}.gamesave-staging-{transactionId}"),
                Path.Combine(parent, $".{Path.GetFileName(target)}.gamesave-safety-{transactionId}"),
                manifest with { Files = files })
            {
                OriginalExisted = Directory.Exists(target)
            });
        }
        if (plans.Count == 0) throw new InvalidDataException("当前电脑没有可用于恢复的已配置存档根目录。");

        string journalPath = Path.Combine(transactionDirectory, "multi-root-journal.json");
        DateTimeOffset createdAt = DateTimeOffset.UtcNow;
        RegistryRestorePreparation? registryPreparation = null;
        RestoreRootPlan? registryPlan = null;
        bool registryApplyStarted = false;
        await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.Prepared, createdAt, cancellationToken);
        try
        {
            foreach (RestoreRootPlan plan in plans)
            {
                plan.State = RestoreRootState.StagingBuilding;
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.BuildingStaging, createdAt, cancellationToken);
                await BuildAndVerifyStagingAsync(server, deviceToken, plan.Manifest, plan.StagingDirectory, cancellationToken);
                plan.State = RestoreRootState.StagingBuilt;
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.StagingBuilt, createdAt, cancellationToken);
            }

            if (registryRules.Count > 0)
            {
                IRegistryRestoreTransaction transaction = registryRestoreTransaction
                    ?? throw new InvalidOperationException("当前环境未提供注册表恢复事务服务。");
                registryPlan = plans.SingleOrDefault(plan => string.Equals(plan.RootId, "registry", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("快照不包含注册表数据，拒绝恢复已配置的注册表规则。");
                registryPreparation = await transaction.PrepareAsync(
                    registryPlan.StagingDirectory, registryRules, transactionDirectory, CancellationToken.None);
            }

            cancellationToken.ThrowIfCancellationRequested();
            validateBeforeApply?.Invoke();

            foreach (RestoreRootPlan plan in plans)
            {
                plan.OriginalExisted = Directory.Exists(plan.TargetDirectory);
                if (plan.OriginalExisted)
                {
                    plan.State = RestoreRootState.MovingOriginal;
                    await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.MovingOriginals, createdAt, CancellationToken.None, registryPreparation);
                    Directory.Move(plan.TargetDirectory, plan.SafetyBackupDirectory);
                    plan.OriginalMoved = true;
                }
                plan.State = RestoreRootState.OriginalMoved;
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.OriginalsMoved, createdAt, CancellationToken.None, registryPreparation);
            }

            foreach (RestoreRootPlan plan in plans)
            {
                plan.State = RestoreRootState.ApplyingTarget;
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.ApplyingTargets, createdAt, CancellationToken.None, registryPreparation);
                Directory.Move(plan.StagingDirectory, plan.TargetDirectory);
                plan.Applied = true;
                plan.State = RestoreRootState.Applied;
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.TargetsApplied, createdAt, CancellationToken.None, registryPreparation);
            }

            foreach (RestoreRootPlan plan in plans)
            {
                plan.State = RestoreRootState.Verifying;
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.Verifying, createdAt, CancellationToken.None, registryPreparation);
                await VerifyDirectoryAsync(plan.TargetDirectory, plan.Manifest.Files, CancellationToken.None);
                plan.State = RestoreRootState.Verified;
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.Verified, createdAt, CancellationToken.None, registryPreparation);
            }

            if (registryPreparation is not null && registryPlan is not null)
            {
                registryPreparation = registryPreparation with
                {
                    SnapshotDirectory = registryPlan.TargetDirectory,
                    State = RegistryRestoreState.Applying
                };
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.Verified, createdAt, CancellationToken.None, registryPreparation);
                registryApplyStarted = true;
                await registryRestoreTransaction!.ApplyAsync(registryPreparation with { State = RegistryRestoreState.Prepared }, CancellationToken.None);
                registryPreparation = registryPreparation with { State = RegistryRestoreState.Applied };
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.Verified, createdAt, CancellationToken.None, registryPreparation);
            }

            await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.Completed, createdAt, CancellationToken.None, registryPreparation);
            TryDeleteDirectory(transactionDirectory);
            foreach (RestoreRootPlan plan in plans) CleanupOldSafetyBackups(plan.TargetDirectory, plan.SafetyBackupDirectory);
            return plans.Select(plan => new RestoreResult(manifest.SnapshotId, plan.TargetDirectory, plan.OriginalMoved ? plan.SafetyBackupDirectory : null)).ToArray();
        }
        catch (Exception restoreFailure)
        {
            try
            {
                Exception? registryRollbackFailure = null;
                if (registryApplyStarted && registryPreparation is not null)
                {
                    registryPreparation = registryPreparation with { State = RegistryRestoreState.RollingBack };
                    await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.RollingBack, createdAt, CancellationToken.None, registryPreparation);
                    try
                    {
                        await registryRestoreTransaction!.RollbackAsync(registryPreparation, CancellationToken.None);
                        registryPreparation = registryPreparation with { State = RegistryRestoreState.RolledBack };
                    }
                    catch (Exception exception) { registryRollbackFailure = exception; }
                }
                foreach (RestoreRootPlan plan in plans)
                {
                    plan.State = RestoreRootState.RollingBack;
                    await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.RollingBack, createdAt, CancellationToken.None, registryPreparation);
                }
                await RollbackPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, createdAt, registryPreparation);
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.RolledBack, createdAt, CancellationToken.None, registryPreparation);
                if (registryRollbackFailure is not null)
                    throw new InvalidOperationException("注册表回滚失败，已保留恢复 Journal 供人工处理。", registryRollbackFailure);
            }
            catch (Exception rollbackFailure)
            {
                Exception effectiveRollbackFailure = rollbackFailure;
                try
                {
                    await PersistPlansAsync(
                        journalPath, transactionId, gameId, manifest.SnapshotId, plans,
                        MultiRootRestoreState.Failed, createdAt, CancellationToken.None,
                        registryPreparation);
                }
                catch (Exception journalFailure)
                {
                    effectiveRollbackFailure = new AggregateException(
                        rollbackFailure, journalFailure);
                }
                throw new RestoreRollbackFailedException(
                    "多目录存档恢复失败且未能完整回滚，已保留安全备份和可用现场；客户端将阻止自动同步。",
                    new AggregateException(restoreFailure, effectiveRollbackFailure));
            }
            throw;
        }
    }
    private static void ValidateRootDirectories(IReadOnlyList<SaveRootRule> roots)
    {
        string[] paths = roots.Select(root => Path.TrimEndingDirectorySeparator(Path.GetFullPath(root.Path))).ToArray();
        for (int left = 0; left < paths.Length; left++)
        for (int right = left + 1; right < paths.Length; right++)
        {
            if (string.Equals(paths[left], paths[right], StringComparison.OrdinalIgnoreCase)
                || paths[left].StartsWith(paths[right] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || paths[right].StartsWith(paths[left] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("存档根目录不能重叠或互为父子目录。");
        }
    }

    private static void ValidateGeneratedRegistryRoot(string path)
    {
        string generatedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GameSaveManager",
            "registry")));
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string relative = Path.GetRelativePath(generatedRoot, target);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Length < 3)
            throw new InvalidOperationException("注册表恢复缓存目录不属于当前应用、服务端、账号和游戏的隔离范围。");

        DirectoryInfo? existing = new(target);
        while (existing is not null && !existing.Exists) existing = existing.Parent;
        if (existing is not null)
            SaveRootTopologyValidator.ValidateNoReparsePointTraversal(existing.FullName, "注册表恢复缓存目录");
    }

    private static async Task RollbackPlansAsync(
        string journalPath,
        string transactionId,
        string gameId,
        string snapshotId,
        IReadOnlyList<RestoreRootPlan> plans,
        DateTimeOffset createdAt,
        RegistryRestorePreparation? registryPreparation)
    {
        foreach (RestoreRootPlan plan in plans.Reverse())
        {
            plan.State = RestoreRootState.RollingBack;
            await PersistPlansAsync(journalPath, transactionId, gameId, snapshotId, plans, MultiRootRestoreState.RollingBack, createdAt, CancellationToken.None, registryPreparation);
            if (plan.Applied && Directory.Exists(plan.TargetDirectory))
            {
                Directory.Delete(plan.TargetDirectory, recursive: true);
            }
            if (plan.OriginalMoved && Directory.Exists(plan.SafetyBackupDirectory) && !Directory.Exists(plan.TargetDirectory))
            {
                Directory.Move(plan.SafetyBackupDirectory, plan.TargetDirectory);
            }
            if (Directory.Exists(plan.StagingDirectory))
            {
                Directory.Delete(plan.StagingDirectory, recursive: true);
            }
            plan.State = RestoreRootState.RolledBack;
            await PersistPlansAsync(journalPath, transactionId, gameId, snapshotId, plans, MultiRootRestoreState.RollingBack, createdAt, CancellationToken.None, registryPreparation);
        }
    }
    private static MultiRootRestoreJournal CreateMultiJournal(string transactionId, string gameId, string snapshotId, IReadOnlyList<RestoreRootPlan> plans, MultiRootRestoreState state, DateTimeOffset createdAt, RegistryRestorePreparation? registryPreparation = null) =>
        new(transactionId, gameId, snapshotId, state, plans.Select(plan => new RestoreRootJournalItem(plan.RootId, plan.TargetDirectory, plan.StagingDirectory, plan.SafetyBackupDirectory, plan.State, plan.OriginalExisted, plan.OriginalMoved, plan.Applied)).ToArray(), createdAt, DateTimeOffset.UtcNow, registryPreparation?.State ?? RegistryRestoreState.NotRequired, registryPreparation?.SafetyDirectory, registryPreparation?.Rules);

    private static async Task WriteMultiJournalAsync(string path, MultiRootRestoreJournal journal, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, journal, JournalJsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private static Task PersistPlansAsync(string path, string transactionId, string gameId, string snapshotId, IReadOnlyList<RestoreRootPlan> plans, MultiRootRestoreState state, DateTimeOffset createdAt, CancellationToken cancellationToken, RegistryRestorePreparation? registryPreparation = null) =>
        WriteMultiJournalAsync(path, CreateMultiJournal(transactionId, gameId, snapshotId, plans, state, createdAt, registryPreparation), cancellationToken);
    private sealed class RestoreRootPlan(string rootId, string targetDirectory, string stagingDirectory, string safetyBackupDirectory, CloudSnapshotManifest manifest)
    {
        public string RootId { get; } = rootId;
        public string TargetDirectory { get; } = targetDirectory;
        public string StagingDirectory { get; } = stagingDirectory;
        public string SafetyBackupDirectory { get; } = safetyBackupDirectory;
        public CloudSnapshotManifest Manifest { get; } = manifest;
        public RestoreRootState State { get; set; } = RestoreRootState.Prepared;
        public bool OriginalExisted { get; set; }
        public bool OriginalMoved { get; set; }
        public bool Applied { get; set; }
    }
    private static SnapshotPathFormat DetectPathFormat(CloudSnapshotManifest manifest, IReadOnlyList<SaveRootRule> roots)
    {
        IReadOnlyList<CloudSnapshotFile> files = manifest.Files;
        string[] declaredRootIds = (manifest.Roots ?? [])
            .Select(root => root.RootId)
            .Where(rootId => !string.IsNullOrWhiteSpace(rootId))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (declaredRootIds.Length > 0)
        {
            string[] declaredPrefixes = declaredRootIds.Select(rootId => rootId + "/").ToArray();
            if (files.Any(file => !declaredPrefixes.Any(prefix =>
                    file.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    && file.RelativePath.Length > prefix.Length)))
                throw new InvalidDataException("快照文件路径与自身声明的根目录元数据不一致，拒绝恢复。");
            return SnapshotPathFormat.NamespacedRoots;
        }

        if (files.Count == 0)
        {
            SaveRootRule[] emptyFileRoots = roots.Where(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (emptyFileRoots.Length != 1)
                throw new InvalidDataException("空的旧版快照没有根目录元数据，只有配置单个文件存档目录时才能安全恢复。");
            return SnapshotPathFormat.LegacySingleRoot;
        }
        string[] prefixes = roots.Select(root => root.RootId + "/").ToArray();
        bool[] namespaced = files.Select(file => prefixes.Any(prefix => file.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))).ToArray();
        if (namespaced.All(value => value)) return SnapshotPathFormat.NamespacedRoots;
        if (namespaced.Any(value => value)) throw new InvalidDataException("快照同时包含旧版单目录路径和新版多根目录路径，拒绝恢复。");
        SaveRootRule[] fileRoots = roots.Where(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (fileRoots.Length != 1) throw new InvalidDataException("该快照使用旧版单目录格式，但当前配置了多个存档目录。请只保留主存档目录后恢复，或手动导出快照。");
        if (!fileRoots[0].UserConfirmed) throw new InvalidOperationException("旧版快照只能恢复到已确认的主存档目录。");
        return SnapshotPathFormat.LegacySingleRoot;
    }
    private async Task<RestoreResult> RestoreOneAsync(
        Uri server,
        string deviceToken,
        CloudSnapshotManifest manifest,
        string saveDirectory,
        CancellationToken cancellationToken,
        Action? validateBeforeApply)
    {
        string target = Path.GetFullPath(saveDirectory);
        if (File.Exists(target))
        {
            throw new IOException($"存档目录路径被同名文件占用: {target}");
        }
        string parent = Path.GetDirectoryName(target)
            ?? throw new IOException("存档目录必须具有父目录");
        Directory.CreateDirectory(parent);

        string transactionId = Guid.NewGuid().ToString("N");
        string transactionDirectory = Path.Combine(RestoreRoot, transactionId);
        string staging = Path.Combine(parent, $".{Path.GetFileName(target)}.gamesave-staging-{transactionId}");
        string safetyBackup = Path.Combine(parent, $".{Path.GetFileName(target)}.gamesave-safety-{transactionId}");
        string journalPath = Path.Combine(transactionDirectory, "journal.json");
        EnsureNoReparsePointTraversal(ApplicationDataRoot, transactionDirectory);
        Directory.CreateDirectory(transactionDirectory);
        EnsureNoReparsePointTraversal(ApplicationDataRoot, transactionDirectory);

        var journal = new RestoreJournal(
            transactionId,
            RestoreJournalState.Prepared,
            target,
            staging,
            safetyBackup,
            manifest.SnapshotId,
            DateTimeOffset.UtcNow);
        await WriteJournalAsync(journalPath, journal, cancellationToken);

        string? actualSafetyBackup = null;
        bool targetApplied = false;
        try
        {
            await BuildAndVerifyStagingAsync(server, deviceToken, manifest, staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            validateBeforeApply?.Invoke();

            if (Directory.Exists(target))
            {
                Directory.Move(target, safetyBackup);
                actualSafetyBackup = safetyBackup;
                journal = journal with { State = RestoreJournalState.OriginalMoved, UpdatedAt = DateTimeOffset.UtcNow };
                await WriteJournalAsync(journalPath, journal, CancellationToken.None);
            }

            Directory.Move(staging, target);
            targetApplied = true;
            journal = journal with { State = RestoreJournalState.Applied, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteJournalAsync(journalPath, journal, CancellationToken.None);

            await VerifyDirectoryAsync(target, manifest.Files, CancellationToken.None);
            journal = journal with { State = RestoreJournalState.Completed, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteJournalAsync(journalPath, journal, CancellationToken.None);
            TryDeleteDirectory(transactionDirectory);
            CleanupOldSafetyBackups(target, actualSafetyBackup);
            return new RestoreResult(manifest.SnapshotId, target, actualSafetyBackup);
        }
        catch (Exception restoreFailure)
        {
            try
            {
                if (actualSafetyBackup is not null && Directory.Exists(actualSafetyBackup))
                {
                    if (Directory.Exists(target)) Directory.Delete(target, recursive: true);
                    Directory.Move(actualSafetyBackup, target);
                }
                else if (targetApplied && Directory.Exists(target))
                {
                    Directory.Delete(target, recursive: true);
                }
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
                if (Directory.Exists(transactionDirectory)) Directory.Delete(transactionDirectory, recursive: true);
            }
            catch (Exception rollbackFailure)
            {
                // 回滚失败时保留 Journal 和现场，启动恢复流程会再次处理。
                throw new RestoreRollbackFailedException(
                    "存档恢复失败且未能完整回滚，已保留恢复 Journal 和安全备份；客户端将阻止自动同步。",
                    new AggregateException(restoreFailure, rollbackFailure));
            }
            throw;
        }
    }

    private async Task<LocalSyncState?> MarkRestorePendingAsync(
        Uri server,
        string userId,
        string gameId,
        string restoredSnapshotId,
        CancellationToken cancellationToken)
    {
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalSyncState? previous = await localSyncStateStore.GetAsync(
            serverKey, userId, gameId, cancellationToken);
        await localSyncStateStore.SaveAsync(
            new LocalSyncState(
                serverKey,
                gameId,
                restoredSnapshotId,
                LocalSyncState.IntentionalRestorePendingVersion,
                userId),
            cancellationToken);
        return previous;
    }

    private async Task TryRestorePreviousStateAsync(
        Uri server,
        string userId,
        string gameId,
        LocalSyncState? previous)
    {
        try
        {
            string serverKey = GameSaveServerIdentity.CreateStableKey(server);
            if (previous is null)
                await localSyncStateStore.DeleteAsync(
                    serverKey, userId, gameId, CancellationToken.None);
            else
                await localSyncStateStore.SaveAsync(previous, CancellationToken.None);
        }
        catch
        {
            // 无法恢复旧基线时保留“待处理恢复”标记更安全；后续检查会阻止自动覆盖或上传。
        }
    }
    /// <summary>
    /// 应用启动时调用。只在“原目录已移走且目标目录不存在”这一确定场景自动回滚；
    /// 其他场景保留现场，避免用猜测覆盖可能已恢复成功的用户数据。
    /// </summary>
    public Task<IReadOnlyList<string>> RecoverInterruptedRestoresAsync(CancellationToken cancellationToken) =>
        RecoverInterruptedRestoresAsync(RestoreRoot, cancellationToken);

    public async Task<IReadOnlyList<string>> RecoverInterruptedRestoresAsync(string restoreRoot, CancellationToken cancellationToken)
    {
        if (!Directory.Exists(restoreRoot))
        {
            return Array.Empty<string>();
        }

        var messages = new List<string>();
        string fullRestoreRoot = Path.GetFullPath(restoreRoot);
        try
        {
            string boundary = PathEquals(fullRestoreRoot, RestoreRoot)
                ? ApplicationDataRoot
                : fullRestoreRoot;
            EnsureNoReparsePointTraversal(boundary, fullRestoreRoot);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return [$"恢复事务目录不安全，已停止自动处理且未修改存档：{exception.Message}"];
        }
        string[] transactionDirectories;
        try
        {
            transactionDirectories = Directory.EnumerateDirectories(fullRestoreRoot)
                .Where(directory => IsOwnedTransactionDirectory(fullRestoreRoot, directory))
                .ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return [$"无法读取存档恢复事务目录，已停止自动处理：{exception.Message}"];
        }

        foreach (string transactionDirectory in transactionDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string journalPath = Path.Combine(transactionDirectory, "multi-root-journal.json");
            if (!File.Exists(journalPath)) continue;
            MultiRootRestoreJournal? journal = await ReadMultiJournalAsync(journalPath, cancellationToken);
            if (journal is null)
            {
                messages.Add($"发现无法读取的多目录恢复日志，已保留现场且未修改存档：{journalPath}");
                continue;
            }
            if (journal.State is MultiRootRestoreState.Completed or MultiRootRestoreState.RolledBack)
            {
                TryDeleteDirectory(transactionDirectory);
                continue;
            }
            if (!TryValidateMultiRootRecoveryJournal(transactionDirectory, journal, out string validationError))
            {
                messages.Add($"发现不安全的多目录恢复日志，已拒绝自动处理：{validationError}");
                continue;
            }
            MultiRootRestoreJournal recovered = await RecoverMultiRootJournalAsync(journalPath, journal, cancellationToken);
            messages.Add(recovered.State == MultiRootRestoreState.RolledBack
                ? $"已回滚未完成的多目录存档恢复: {journal.SnapshotId}"
                : $"发现无法自动判定的多目录存档恢复，已保留现场等待人工处理: {journal.SnapshotId}");
        }

        foreach (string transactionDirectory in transactionDirectories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string journalPath = Path.Combine(transactionDirectory, "journal.json");
            if (!File.Exists(journalPath)) continue;
            RestoreJournal? journal = await ReadJournalAsync(journalPath, cancellationToken);
            if (journal is null)
            {
                messages.Add($"发现无法读取的恢复日志，已保留现场且未修改存档：{journalPath}");
                continue;
            }
            if (journal.State is RestoreJournalState.Completed)
            {
                TryDeleteDirectory(transactionDirectory);
                continue;
            }
            if (!TryValidateSingleRootRecoveryJournal(transactionDirectory, journal, out string validationError))
            {
                messages.Add($"发现不安全的恢复日志，已拒绝自动处理：{validationError}");
                continue;
            }

            bool targetExists = Directory.Exists(journal.SaveDirectory);
            bool backupExists = !string.IsNullOrWhiteSpace(journal.SafetyBackupDirectory)
                                && Directory.Exists(journal.SafetyBackupDirectory);
            if (!targetExists && backupExists && journal.State is RestoreJournalState.Prepared or RestoreJournalState.OriginalMoved)
            {
                Directory.Move(journal.SafetyBackupDirectory!, journal.SaveDirectory);
                messages.Add($"已从安全备份回滚未完成的存档恢复: {journal.SaveDirectory}");
            }
            else
            {
                messages.Add($"发现未完成的存档恢复，已保留现场等待处理: {journal.SaveDirectory}");
            }
        }
        return messages;
    }

    private static bool IsOwnedTransactionDirectory(string restoreRoot, string directory)
    {
        try
        {
            string fullDirectory = Path.GetFullPath(directory);
            if (!string.Equals(Path.GetDirectoryName(fullDirectory), restoreRoot, StringComparison.OrdinalIgnoreCase)
                || !Guid.TryParseExact(Path.GetFileName(fullDirectory), "N", out _)
                || (File.GetAttributes(fullDirectory) & FileAttributes.ReparsePoint) != 0)
                return false;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static bool TryValidateMultiRootRecoveryJournal(
        string transactionDirectory,
        MultiRootRestoreJournal journal,
        out string error)
    {
        if (!TryValidateTransactionIdentity(transactionDirectory, journal.TransactionId, journal.SnapshotId, out error))
            return false;
        if (journal.Roots is null || journal.Roots.Count is < 1 or > GameSaveProtocolLimits.MaximumSnapshotRoots)
        {
            error = "恢复根目录数量无效。";
            return false;
        }
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var targets = new List<string>(journal.Roots.Count);
        foreach (RestoreRootJournalItem? root in journal.Roots)
        {
            if (root is null || string.IsNullOrWhiteSpace(root.RootId) || !ids.Add(root.RootId)
                || !TryValidateGeneratedRecoveryPaths(
                    journal.TransactionId, root.TargetDirectory, root.StagingDirectory,
                    root.SafetyBackupDirectory, out string target, out error))
                return false;
            targets.Add(target);
        }
        for (int left = 0; left < targets.Count; left++)
        for (int right = left + 1; right < targets.Count; right++)
        {
            if (PathsOverlap(targets[left], targets[right]))
            {
                error = "恢复目标目录存在重叠。";
                return false;
            }
        }

        if (!string.IsNullOrWhiteSpace(journal.RegistrySafetyDirectory)
            && !PathEquals(
                journal.RegistrySafetyDirectory,
                Path.Combine(transactionDirectory, "registry-safety")))
        {
            error = "注册表安全备份目录不属于当前恢复事务。";
            return false;
        }
        IReadOnlyList<RegistrySaveRule> registryRules = journal.RegistryRules ?? [];
        if (registryRules.Count > GameSaveProtocolLimits.MaximumSnapshotRoots
            || registryRules.Any(rule => rule is null
                                         || !rule.UserConfirmed
                                         || string.IsNullOrWhiteSpace(rule.RuleId)
                                         || string.IsNullOrWhiteSpace(rule.KeyPath)
                                         || !(rule.KeyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
                                              || rule.KeyPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase))))
        {
            error = "注册表恢复规则无效或未经确认。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryValidateSingleRootRecoveryJournal(
        string transactionDirectory,
        RestoreJournal journal,
        out string error)
    {
        if (!TryValidateTransactionIdentity(transactionDirectory, journal.TransactionId, journal.SnapshotId, out error))
            return false;
        return TryValidateGeneratedRecoveryPaths(
            journal.TransactionId,
            journal.SaveDirectory,
            journal.StagingDirectory,
            journal.SafetyBackupDirectory,
            out _,
            out error);
    }

    private static bool TryValidateTransactionIdentity(
        string transactionDirectory,
        string? transactionId,
        string? snapshotId,
        out string error)
    {
        if (string.IsNullOrWhiteSpace(transactionId)
            || !Guid.TryParseExact(transactionId, "N", out _)
            || !string.Equals(transactionId, Path.GetFileName(transactionDirectory), StringComparison.OrdinalIgnoreCase)
            || string.IsNullOrWhiteSpace(snapshotId)
            || snapshotId.Length > 256)
        {
            error = "事务身份或快照身份无效。";
            return false;
        }
        error = string.Empty;
        return true;
    }

    private static bool TryValidateGeneratedRecoveryPaths(
        string transactionId,
        string? targetPath,
        string? stagingPath,
        string? safetyPath,
        out string normalizedTarget,
        out string error)
    {
        normalizedTarget = string.Empty;
        try
        {
            if (string.IsNullOrWhiteSpace(targetPath)
                || string.IsNullOrWhiteSpace(stagingPath)
                || string.IsNullOrWhiteSpace(safetyPath))
            {
                error = "恢复路径不完整。";
                return false;
            }
            normalizedTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
            string? parent = Path.GetDirectoryName(normalizedTarget);
            string leaf = Path.GetFileName(normalizedTarget);
            if (string.IsNullOrWhiteSpace(parent)
                || string.IsNullOrWhiteSpace(leaf)
                || string.Equals(normalizedTarget, Path.GetPathRoot(normalizedTarget), StringComparison.OrdinalIgnoreCase))
            {
                error = "恢复目标不能是磁盘根目录。";
                return false;
            }
            string expectedStaging = Path.Combine(parent, $".{leaf}.gamesave-staging-{transactionId}");
            string expectedSafety = Path.Combine(parent, $".{leaf}.gamesave-safety-{transactionId}");
            if (!PathEquals(stagingPath, expectedStaging) || !PathEquals(safetyPath, expectedSafety))
            {
                error = "临时目录或安全备份目录不符合恢复事务命名规则。";
                return false;
            }
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            error = $"恢复路径无效：{exception.Message}";
            return false;
        }
    }

    private static bool PathEquals(string first, string second)
    {
        try
        {
            return string.Equals(
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(first)),
                Path.TrimEndingDirectorySeparator(Path.GetFullPath(second)),
                StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static bool PathsOverlap(string first, string second) =>
        PathEquals(first, second)
        || first.StartsWith(second + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
        || second.StartsWith(first + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    private static async Task<MultiRootRestoreJournal?> ReadMultiJournalAsync(string path, CancellationToken cancellationToken)
    {
        try { return JsonSerializer.Deserialize<MultiRootRestoreJournal>(await File.ReadAllTextAsync(path, cancellationToken), JournalJsonOptions); }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException) { return null; }
    }

    private async Task<MultiRootRestoreJournal> RecoverMultiRootJournalAsync(
        string journalPath,
        MultiRootRestoreJournal journal,
        CancellationToken cancellationToken)
    {
        RestoreRootJournalItem[] roots = journal.Roots.ToArray();
        bool requiresManualIntervention = false;
        for (int index = roots.Length - 1; index >= 0; index--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreRootJournalItem root = roots[index];
            RestoreRootState observedState = root.State;
            roots[index] = root with { State = RestoreRootState.RollingBack };
            journal = journal with { State = MultiRootRestoreState.RollingBack, Roots = roots, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);

            bool targetExists = Directory.Exists(root.TargetDirectory);
            bool safetyExists = Directory.Exists(root.SafetyBackupDirectory);
            bool recovered = false;
            if (!targetExists && safetyExists)
            {
                Directory.Move(root.SafetyBackupDirectory, root.TargetDirectory);
                recovered = true;
            }
            else if (targetExists && safetyExists && (root.TargetApplied || observedState is RestoreRootState.ApplyingTarget or RestoreRootState.Applied or RestoreRootState.Verifying or RestoreRootState.Verified))
            {
                Directory.Delete(root.TargetDirectory, recursive: true);
                Directory.Move(root.SafetyBackupDirectory, root.TargetDirectory);
                recovered = true;
            }
            else if (targetExists && !safetyExists && !root.OriginalExisted)
            {
                Directory.Delete(root.TargetDirectory, recursive: true);
                recovered = true;
            }
            else if (!targetExists && !safetyExists && !root.OriginalExisted)
            {
                recovered = true;
            }
            else if (targetExists && !safetyExists && root.OriginalExisted
                     && observedState is RestoreRootState.Prepared or RestoreRootState.StagingBuilding or RestoreRootState.StagingBuilt or RestoreRootState.MovingOriginal)
            {
                recovered = true;
            }
            else
            {
                requiresManualIntervention = true;
            }

            if (recovered && Directory.Exists(root.StagingDirectory))
            {
                Directory.Delete(root.StagingDirectory, recursive: true);
            }
            roots[index] = root with { State = recovered ? RestoreRootState.RolledBack : RestoreRootState.Failed };
            journal = journal with { State = MultiRootRestoreState.RollingBack, Roots = roots, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);
        }

        if (journal.RegistryState is RegistryRestoreState.Prepared or RegistryRestoreState.Applying or RegistryRestoreState.Applied or RegistryRestoreState.RollingBack)
        {
            IReadOnlyList<RegistrySaveRule> rules = journal.RegistryRules ?? [];
            if (journal.RegistryState == RegistryRestoreState.Prepared)
            {
                journal = journal with { RegistryState = RegistryRestoreState.RolledBack, UpdatedAt = DateTimeOffset.UtcNow };
                await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);
            }
            else if (registryRestoreTransaction is null || string.IsNullOrWhiteSpace(journal.RegistrySafetyDirectory) || rules.Count == 0)
            {
                requiresManualIntervention = true;
                journal = journal with { RegistryState = RegistryRestoreState.Failed, UpdatedAt = DateTimeOffset.UtcNow };
                await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);
            }
            else
            {
                journal = journal with { RegistryState = RegistryRestoreState.RollingBack, UpdatedAt = DateTimeOffset.UtcNow };
                await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);
                try
                {
                    await registryRestoreTransaction.RollbackAsync(
                        new RegistryRestorePreparation(journal.RegistrySafetyDirectory, string.Empty, rules, RegistryRestoreState.RollingBack),
                        CancellationToken.None);
                    journal = journal with { RegistryState = RegistryRestoreState.RolledBack, UpdatedAt = DateTimeOffset.UtcNow };
                    await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);
                }
                catch
                {
                    requiresManualIntervention = true;
                    journal = journal with { RegistryState = RegistryRestoreState.Failed, UpdatedAt = DateTimeOffset.UtcNow };
                    await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);
                }
            }
        }

        MultiRootRestoreState finalState = requiresManualIntervention ? MultiRootRestoreState.Failed : MultiRootRestoreState.RolledBack;
        journal = journal with { State = finalState, Roots = roots, UpdatedAt = DateTimeOffset.UtcNow };
        await WriteMultiJournalAsync(journalPath, journal, CancellationToken.None);
        return journal;
    }
    private async Task BuildAndVerifyStagingAsync(
        Uri server,
        string deviceToken,
        CloudSnapshotManifest manifest,
        string staging,
        CancellationToken cancellationToken)
    {
        EnsureAvailableSpace(staging, manifest.Files.Sum(file => file.Size));
        if (Directory.Exists(staging))
        {
            throw new IOException($"恢复临时目录已存在，拒绝覆盖: {staging}");
        }
        Directory.CreateDirectory(staging);

        foreach (CloudSnapshotFile file in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string cached = await objectCache.GetOrDownloadAsync(
                file,
                (temporary, token) => apiClient.DownloadObjectAsync(
                    server, deviceToken, file.ObjectId, temporary, file.Size, token),
                cancellationToken);
            string destination = ResolveContainedPath(staging, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(cached, destination, overwrite: false);
        }
        await VerifyDirectoryAsync(staging, manifest.Files, cancellationToken);
    }

    private static void EnsureAvailableSpace(string destination, long contentSize)
    {
        string fullPath = Path.GetFullPath(destination);
        string root = Path.GetPathRoot(fullPath) ?? throw new IOException("无法确定恢复目标磁盘。");
        long required = checked(contentSize + Math.Max(100L * 1024 * 1024, contentSize / 10));
        long available = new DriveInfo(root).AvailableFreeSpace;
        if (available < required)
            throw new IOException($"恢复空间不足：至少需要 {required} 字节可用空间，当前只有 {available} 字节。");
    }

    private static void CleanupOldSafetyBackups(string targetDirectory, string? currentSafetyBackup)
    {
        try
        {
            string? parent = Path.GetDirectoryName(Path.GetFullPath(targetDirectory));
            if (parent is null || !Directory.Exists(parent)) return;
            string pattern = $".{Path.GetFileName(targetDirectory)}.gamesave-safety-*";
            DirectoryInfo[] candidates = new DirectoryInfo(parent)
                .EnumerateDirectories(pattern, SearchOption.TopDirectoryOnly)
                .Where(directory =>
                {
                    string transactionId = directory.Name[(pattern.Length - 1)..];
                    return Guid.TryParseExact(transactionId, "N", out _)
                           && (directory.Attributes & FileAttributes.ReparsePoint) == 0;
                })
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .ToArray();
            HashSet<string> keep = candidates.Take(3)
                .Select(directory => directory.FullName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (!string.IsNullOrWhiteSpace(currentSafetyBackup)) keep.Add(Path.GetFullPath(currentSafetyBackup));
            DateTime threshold = DateTime.UtcNow.AddDays(-30);
            foreach (DirectoryInfo candidate in candidates)
            {
                if (keep.Contains(candidate.FullName) || candidate.LastWriteTimeUtc >= threshold) continue;
                try { candidate.Delete(recursive: true); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private async Task VerifyDirectoryAsync(
        string root,
        IReadOnlyList<CloudSnapshotFile> files,
        CancellationToken cancellationToken)
    {
        foreach (CloudSnapshotFile file in files)
        {
            string path = ResolveContainedPath(root, file.RelativePath);
            var info = new FileInfo(path);
            if (!info.Exists || info.Length != file.Size)
            {
                throw new InvalidDataException($"恢复文件大小校验失败: {file.RelativePath}");
            }
            string hash = await fileHashService.ComputeSha256Async(path, cancellationToken);
            if (!string.Equals(hash, file.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException($"恢复文件 SHA-256 校验失败: {file.RelativePath}");
            }
        }
    }

    private static void ValidateManifest(
        CloudSnapshotManifest manifest,
        string expectedGameId,
        string expectedSnapshotId)
    {
        CloudApiResponseValidator.ValidateManifest(manifest, expectedGameId, expectedSnapshotId);
        if (string.IsNullOrWhiteSpace(manifest.SnapshotId) || manifest.Files is null)
        {
            throw new InvalidDataException("云端快照 Manifest 不完整");
        }
        if (!string.Equals(manifest.GameId, expectedGameId, StringComparison.Ordinal)
            || !string.Equals(manifest.SnapshotId, expectedSnapshotId, StringComparison.Ordinal))
            throw new InvalidDataException("服务端返回的快照身份与请求的游戏或快照不一致，已拒绝恢复。");
        if (manifest.Files.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            throw new InvalidDataException("云端快照超过客户端允许的文件数量上限。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (manifest.Roots is { } roots)
        {
            if (roots.Count > GameSaveProtocolLimits.MaximumSnapshotRoots)
                throw new InvalidDataException("云端快照超过客户端允许的根目录数量上限。");
            var rootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (CloudSnapshotRoot root in roots)
            {
                if (root is null || string.IsNullOrWhiteSpace(root.RootId)
                    || root.RootId.Length > GameSaveProtocolLimits.RootIdMaxLength
                    || root.RootId.Contains('/') || root.RootId.Contains('\\')
                    || !rootIds.Add(root.RootId))
                    throw new InvalidDataException("云端快照包含无效或重复的根目录标识。");
                if (!string.Equals(root.RootType, "FILE", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(root.RootType, "REGISTRY", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException($"云端快照包含未知根目录类型：{root.RootType}");
            }
        }
        foreach (CloudSnapshotFile file in manifest.Files)
        {
            if (file is null)
                throw new InvalidDataException("云端快照包含空文件描述。");
            if (file.Size < 0 || string.IsNullOrWhiteSpace(file.ObjectId)
                || string.IsNullOrWhiteSpace(file.Sha256) || file.Sha256.Length != 64
                || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"云端快照包含无效的内容描述：{file.RelativePath}");
            if (!paths.Add(file.RelativePath))
            {
                throw new InvalidDataException($"云端快照包含重复路径: {file.RelativePath}");
            }
            _ = ResolveContainedPath(Path.GetTempPath(), file.RelativePath);
        }
    }

    private static string GetNamespacedRootId(string relativePath)
    {
        int separator = relativePath.IndexOf('/');
        if (separator <= 0 || separator == relativePath.Length - 1)
            throw new InvalidDataException($"快照文件路径缺少有效的根目录命名空间：{relativePath}");
        return relativePath[..separator];
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)
            || relativePath.Length > GameSaveProtocolLimits.RelativePathMaxLength
            || Path.IsPathRooted(relativePath))
        {
            throw new InvalidDataException("快照文件路径必须是非空相对路径");
        }
        string rootPath = Path.GetFullPath(root);
        string candidate = Path.GetFullPath(Path.Combine(
            rootPath,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(rootPath, candidate);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidDataException($"快照文件路径越过存档目录边界: {relativePath}");
        }
        return candidate;
    }

    private static async Task WriteJournalAsync(
        string path,
        RestoreJournal journal,
        CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await JsonSerializer.SerializeAsync(stream, journal, JournalJsonOptions, cancellationToken);
                await stream.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            TryDeleteFile(temporary);
        }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void TryDeleteDirectory(string path)
    {
        try { if (Directory.Exists(path)) Directory.Delete(path, recursive: true); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void EnsureNoReparsePointTraversal(string boundaryPath, string targetPath)
    {
        string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(boundaryPath));
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(targetPath));
        string relative = Path.GetRelativePath(boundary, target);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("恢复事务目录越过应用数据边界。");

        string current = boundary;
        if (Directory.Exists(current)
            && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("恢复事务目录包含重解析点。");
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".") continue;
            current = Path.Combine(current, segment);
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("恢复事务目录包含重解析点。");
        }
    }

    private static async Task<RestoreJournal?> ReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<RestoreJournal>(json, JournalJsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
