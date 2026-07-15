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

    private static readonly string RestoreRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameSaveManager",
        "restore");

    public async Task<RestoreResult> RestoreAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        string saveDirectory,
        CancellationToken cancellationToken)
    {
        CloudSnapshotManifest manifest = await apiClient.GetSnapshotAsync(server, deviceToken, gameId, snapshotId, cancellationToken);
        ValidateManifest(manifest);
        CloudHead remoteHead = await apiClient.GetHeadAsync(server, deviceToken, gameId, cancellationToken);
        RestoreResult result = await RestoreOneAsync(server, deviceToken, manifest, remoteHead, saveDirectory, cancellationToken);
        await SaveRestoreStateAsync(server, gameId, remoteHead, cancellationToken);
        return result;
    }

    /// <summary>按存档根标识拆分云端文件，分别恢复到每个已确认的本地目录。</summary>
    public Task<IReadOnlyList<RestoreResult>> RestoreAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        IReadOnlyList<SaveRootRule> roots,
        CancellationToken cancellationToken) =>
        RestoreAsync(server, deviceToken, gameId, snapshotId, roots, [], cancellationToken);

    public async Task<IReadOnlyList<RestoreResult>> RestoreAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        IReadOnlyList<SaveRootRule> roots,
        IReadOnlyList<RegistrySaveRule> registryRules,
        CancellationToken cancellationToken)
    {
        if (roots is null || roots.Count == 0) throw new InvalidOperationException("至少需要一个存档根目录。");
        if (roots.Any(root => !root.UserConfirmed)) throw new InvalidOperationException("所有存档根目录都必须经用户确认。");
        if (roots.Select(root => root.RootId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != roots.Count)
            throw new InvalidOperationException("存档根目录标识不能重复。");

        CloudSnapshotManifest manifest = await apiClient.GetSnapshotAsync(server, deviceToken, gameId, snapshotId, cancellationToken);
        ValidateManifest(manifest);
        SnapshotPathFormat pathFormat = DetectPathFormat(manifest.Files, roots);
        CloudHead remoteHead = await apiClient.GetHeadAsync(server, deviceToken, gameId, cancellationToken);
        if (pathFormat == SnapshotPathFormat.LegacySingleRoot)
        {
            if (registryRules.Count > 0) throw new InvalidDataException("旧版单根快照不包含注册表数据，拒绝混合恢复。");
            SaveRootRule root = roots.Single(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase));
            RestoreResult result = await RestoreOneAsync(server, deviceToken, manifest, remoteHead, root.Path, cancellationToken);
            await SaveRestoreStateAsync(server, gameId, remoteHead, cancellationToken);
            return [result];
        }
        IReadOnlyList<RestoreResult> results = await RestoreNamespacedRootsAsync(server, deviceToken, gameId, manifest, roots, registryRules, cancellationToken);
        await SaveRestoreStateAsync(server, gameId, remoteHead, cancellationToken);
        return results;
    }

    private async Task<IReadOnlyList<RestoreResult>> RestoreNamespacedRootsAsync(
        Uri server, string deviceToken, string gameId, CloudSnapshotManifest manifest,
        IReadOnlyList<SaveRootRule> roots, IReadOnlyList<RegistrySaveRule> registryRules, CancellationToken cancellationToken)
    {
        ValidateRootDirectories(roots);
        string transactionId = Guid.NewGuid().ToString("N");
        string transactionDirectory = Path.Combine(RestoreRoot, transactionId);
        Directory.CreateDirectory(transactionDirectory);
        var plans = new List<RestoreRootPlan>();
        foreach (SaveRootRule root in roots)
        {
            string prefix = root.RootId + "/";
            IReadOnlyList<CloudSnapshotFile> files = manifest.Files.Where(file => file.RelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                .Select(file => file with { RelativePath = file.RelativePath[prefix.Length..] }).ToArray();
            if (files.Count == 0) continue;
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
        if (plans.Count == 0) throw new InvalidDataException("快照不包含已配置存档根目录的文件。");

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
            Directory.Delete(transactionDirectory, recursive: true);
            return plans.Select(plan => new RestoreResult(manifest.SnapshotId, plan.TargetDirectory, plan.OriginalMoved ? plan.SafetyBackupDirectory : null)).ToArray();
        }
        catch
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
            catch
            {
                await PersistPlansAsync(journalPath, transactionId, gameId, manifest.SnapshotId, plans, MultiRootRestoreState.Failed, createdAt, CancellationToken.None, registryPreparation);
            }
            throw;
        }
    }
    private static void ValidateRootDirectories(IReadOnlyList<SaveRootRule> roots)
    {
        string[] paths = roots.Select(root => Path.GetFullPath(root.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToArray();
        for (int left = 0; left < paths.Length; left++)
        for (int right = left + 1; right < paths.Length; right++)
        {
            if (string.Equals(paths[left], paths[right], StringComparison.OrdinalIgnoreCase)
                || paths[left].StartsWith(paths[right] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                || paths[right].StartsWith(paths[left] + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("存档根目录不能重叠或互为父子目录。");
        }
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
        catch
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            throw;
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
    private static SnapshotPathFormat DetectPathFormat(IReadOnlyList<CloudSnapshotFile> files, IReadOnlyList<SaveRootRule> roots)
    {
        if (files.Count == 0) throw new InvalidDataException("快照不包含文件。");
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
        CloudHead remoteHead,
        string saveDirectory,
        CancellationToken cancellationToken)
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
        Directory.CreateDirectory(transactionDirectory);

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
        try
        {
            await BuildAndVerifyStagingAsync(server, deviceToken, manifest, staging, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (Directory.Exists(target))
            {
                Directory.Move(target, safetyBackup);
                actualSafetyBackup = safetyBackup;
                journal = journal with { State = RestoreJournalState.OriginalMoved, UpdatedAt = DateTimeOffset.UtcNow };
                await WriteJournalAsync(journalPath, journal, CancellationToken.None);
            }

            Directory.Move(staging, target);
            journal = journal with { State = RestoreJournalState.Applied, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteJournalAsync(journalPath, journal, CancellationToken.None);

            await VerifyDirectoryAsync(target, manifest.Files, CancellationToken.None);
            journal = journal with { State = RestoreJournalState.Completed, UpdatedAt = DateTimeOffset.UtcNow };
            await WriteJournalAsync(journalPath, journal, CancellationToken.None);
            Directory.Delete(transactionDirectory, recursive: true);
            return new RestoreResult(manifest.SnapshotId, target, actualSafetyBackup);
        }
        catch
        {
            throw;
        }
    }

    private Task SaveRestoreStateAsync(Uri server, string gameId, CloudHead remoteHead, CancellationToken cancellationToken) =>
        localSyncStateStore.SaveAsync(new LocalSyncState(GameSaveServerIdentity.CreateStableKey(server), gameId, remoteHead.HeadSnapshotId, remoteHead.Version), cancellationToken);
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
        foreach (string transactionDirectory in Directory.EnumerateDirectories(restoreRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string journalPath = Path.Combine(transactionDirectory, "multi-root-journal.json");
            if (!File.Exists(journalPath)) continue;
            MultiRootRestoreJournal? journal = await ReadMultiJournalAsync(journalPath, cancellationToken);
            if (journal is null || journal.State is MultiRootRestoreState.Completed or MultiRootRestoreState.RolledBack) continue;
            MultiRootRestoreJournal recovered = await RecoverMultiRootJournalAsync(journalPath, journal, cancellationToken);
            messages.Add(recovered.State == MultiRootRestoreState.RolledBack
                ? $"已回滚未完成的多目录存档恢复: {journal.SnapshotId}"
                : $"发现无法自动判定的多目录存档恢复，已保留现场等待人工处理: {journal.SnapshotId}");
        }

        foreach (string journalPath in Directory.EnumerateFiles(restoreRoot, "journal.json", SearchOption.AllDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            RestoreJournal? journal = await ReadJournalAsync(journalPath, cancellationToken);
            if (journal is null || journal.State is RestoreJournalState.Completed) continue;

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

    private static async Task<MultiRootRestoreJournal?> ReadMultiJournalAsync(string path, CancellationToken cancellationToken)
    {
        try { return JsonSerializer.Deserialize<MultiRootRestoreJournal>(await File.ReadAllTextAsync(path, cancellationToken), JournalJsonOptions); }
        catch (JsonException) { return null; }
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
                    server, deviceToken, file.ObjectId, temporary, token),
                cancellationToken);
            string destination = ResolveContainedPath(staging, file.RelativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(cached, destination, overwrite: false);
        }
        await VerifyDirectoryAsync(staging, manifest.Files, cancellationToken);
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

    private static void ValidateManifest(CloudSnapshotManifest manifest)
    {
        if (string.IsNullOrWhiteSpace(manifest.SnapshotId) || manifest.Files is null)
        {
            throw new InvalidDataException("云端快照 Manifest 不完整");
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudSnapshotFile file in manifest.Files)
        {
            if (!paths.Add(file.RelativePath))
            {
                throw new InvalidDataException($"云端快照包含重复路径: {file.RelativePath}");
            }
            _ = ResolveContainedPath(Path.GetTempPath(), file.RelativePath);
        }
    }

    private static string ResolveContainedPath(string root, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath))
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
        string temporary = path + ".tmp";
        string json = JsonSerializer.Serialize(journal, JournalJsonOptions);
        await File.WriteAllTextAsync(temporary, json, cancellationToken);
        File.Move(temporary, path, overwrite: true);
    }

    private static async Task<RestoreJournal?> ReadJournalAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            return JsonSerializer.Deserialize<RestoreJournal>(json, JournalJsonOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}