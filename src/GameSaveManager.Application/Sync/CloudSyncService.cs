using GameSaveManager.Application.Api;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Games;
using GameSaveManager.Domain.Snapshots;
using System.Diagnostics;
using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Application.Sync;

/// <summary>
/// 客户端主动同步编排。
/// 本地确认 HEAD 与远端 HEAD 不一致时立即阻断，禁止把落后设备的存档静默接到最新云端版本之后。
/// </summary>
public sealed class CloudSyncService(
    SaveManifestBuilder manifestBuilder,
    IGameSaveApiClient apiClient,
    ILocalSyncStateStore localSyncStateStore,
    ISavePathTemplateService pathTemplateService)
{
    public Task DeleteLocalStateAsync(Uri server, string userId, string gameId, CancellationToken cancellationToken) =>
        localSyncStateStore.DeleteAsync(GameSaveServerIdentity.CreateStableKey(server), userId, gameId, cancellationToken);

    public Task<CloudSyncResult> SyncAsync(Uri server, string deviceToken, string userId, string gameId, string saveDirectory, SnapshotTrigger trigger, string? description, CancellationToken cancellationToken, bool keepLocalOnConflict = false, IProgress<CloudSyncProgress>? progress = null, bool allowDestructiveChanges = false) =>
        SyncCoreAsync(server, deviceToken, userId, gameId, [SaveRootRule.CreateDefault(saveDirectory, Discovery.SaveLocationSource.Manual, 100, true)], trigger, description, cancellationToken, keepLocalOnConflict, progress, allowDestructiveChanges);

    public Task<CloudSyncResult> SyncAsync(Uri server, string deviceToken, string userId, string gameId, IReadOnlyList<SaveRootRule> saveRoots, SnapshotTrigger trigger, string? description, CancellationToken cancellationToken, bool keepLocalOnConflict = false, IProgress<CloudSyncProgress>? progress = null, bool allowDestructiveChanges = false) =>
        SyncCoreAsync(server, deviceToken, userId, gameId, saveRoots, trigger, description, cancellationToken, keepLocalOnConflict, progress, allowDestructiveChanges);

    private async Task<CloudSyncResult> SyncCoreAsync(
        Uri server,
        string deviceToken,
        string userId,
        string gameId,
        IReadOnlyList<SaveRootRule> saveRoots,
        SnapshotTrigger trigger,
        string? description,
        CancellationToken cancellationToken,
        bool keepLocalOnConflict = false,
        IProgress<CloudSyncProgress>? progress = null,
        bool allowDestructiveChanges = false)
    {
        long startedAt = Stopwatch.GetTimestamp();
        progress?.Report(new CloudSyncProgress("准备", 0, 0, "正在比较本机与云端版本…"));
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalSyncState? localState = await localSyncStateStore.GetAsync(
            serverKey,
            userId,
            gameId,
            cancellationToken);
        CloudHead remoteHead = await apiClient.GetHeadAsync(server, deviceToken, gameId, cancellationToken);
        ValidateHead(remoteHead, gameId);

        progress?.Report(new CloudSyncProgress("扫描", 0, 0, "正在扫描存档并计算内容 Hash…"));
        IReadOnlyList<SnapshotFile> manifest = await manifestBuilder.BuildAsync(saveRoots, cancellationToken);
        if (manifest.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            throw new InvalidOperationException(GameSaveProtocolLimits.ManifestFileLimitMessage);
        IReadOnlyList<SnapshotRootDescriptor> rootDescriptors = BuildRootDescriptors(saveRoots);

        if (!HeadsMatch(localState, remoteHead) && !keepLocalOnConflict)
        {
            if (await TryRepairMatchingRemoteHeadAsync(
                    server, deviceToken, userId, gameId, serverKey, remoteHead, manifest, cancellationToken))
            {
                return new CloudSyncResult(
                    CloudSyncStatus.Success,
                    "本机内容与云端 HEAD 完全一致，已自动修复本机同步基线。",
                    remoteHead.HeadSnapshotId,
                    0,
                    manifest.Count,
                    manifest.Sum(file => file.Size),
                    Stopwatch.GetElapsedTime(startedAt));
            }
            return new CloudSyncResult(
                CloudSyncStatus.RemoteAhead,
                "云端 HEAD 与本机最后确认版本不一致，已阻止上传；需要先进入冲突处理或恢复流程。",
                null,
                0,
                manifest.Count,
                manifest.Sum(file => file.Size),
                Stopwatch.GetElapsedTime(startedAt),
                remoteHead.HeadSnapshotId);
        }

        CloudSnapshotManifest? baseline = remoteHead.HeadSnapshotId is null
            ? null
            : await apiClient.GetSnapshotAsync(server, deviceToken, gameId, remoteHead.HeadSnapshotId, cancellationToken);
        if (baseline is not null)
            ValidateCloudManifest(baseline, gameId, remoteHead.HeadSnapshotId!);
        int removedFileCount = baseline is null ? 0 : CountRemovedFiles(baseline.Files, manifest);
        if (!allowDestructiveChanges && baseline is not null
            && IsDestructiveChange(baseline.Files.Count, manifest.Count, removedFileCount))
            throw new DestructiveSnapshotChangeException(baseline.Files.Count, manifest.Count, removedFileCount);

        // 用户显式选择“保留本机版本”时，以当前云端 HEAD 为父快照提交；旧云端版本保留在时间线中。

        IReadOnlyList<ContentObjectDescriptor> objectDescriptors = manifest
            .Select(file => new ContentObjectDescriptor(file.Sha256, file.Size))
            .Distinct()
            .ToArray();
        progress?.Report(new CloudSyncProgress("比对", 0, objectDescriptors.Count, "正在检查云端缺失内容…"));
        IReadOnlyList<ContentObjectDescriptor> missing = await apiClient.CheckMissingAsync(
            server, deviceToken, objectDescriptors, cancellationToken);
        ValidateMissingObjects(objectDescriptors, missing);

        Dictionary<string, SnapshotFile> sourceFiles = manifest
            .GroupBy(
                file => DescriptorKey(new ContentObjectDescriptor(file.Sha256, file.Size)),
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
        int uploaded = 0;
        foreach (ContentObjectDescriptor descriptor in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sourceFiles.TryGetValue(DescriptorKey(descriptor), out SnapshotFile? source))
            {
                throw new InvalidOperationException("服务端返回了本次 Manifest 中不存在的缺失对象");
            }

            string fullPath = ResolveSourcePath(saveRoots, source.RelativePath);
            await apiClient.UploadObjectAsync(
                server, deviceToken, fullPath, descriptor, cancellationToken);
            uploaded++;
            progress?.Report(new CloudSyncProgress("上传", uploaded, missing.Count, $"正在上传缺失内容（{uploaded}/{missing.Count}）…"));
        }

        progress?.Report(new CloudSyncProgress("复核", uploaded, missing.Count, "正在确认备份期间存档没有再次变化…"));
        IReadOnlyList<SnapshotFile> verifiedManifest = await manifestBuilder.BuildAsync(saveRoots, cancellationToken);
        if (!LocalManifestsMatch(manifest, verifiedManifest))
            throw new IOException("备份期间存档文件发生了变化，本次未提交快照。请完全退出游戏并等待文件写入结束后重试。");

        progress?.Report(new CloudSyncProgress("提交", missing.Count, missing.Count, "正在提交不可变快照…"));
        CloudSnapshotResult committed;
        try
        {
            committed = await apiClient.CommitSnapshotAsync(
                server,
                deviceToken,
                gameId,
                remoteHead.HeadSnapshotId,
                trigger,
                description,
                rootDescriptors,
                manifest,
                cancellationToken);
            ValidateCommitResult(committed, manifest, remoteHead);
            CloudSnapshotManifest committedManifest = await apiClient.GetSnapshotAsync(
                server, deviceToken, gameId, committed.SnapshotId, cancellationToken);
            ValidateCloudManifest(committedManifest, gameId, committed.SnapshotId);
            if (!ManifestsMatch(manifest, committedManifest.Files)
                || !RootDescriptorsMatch(rootDescriptors, committedManifest.Roots))
                throw new InvalidDataException("服务端提交后的快照内容或存档路径元数据与本机 Manifest 不一致，已拒绝更新本机同步基线。");
        }
        catch (Exception original) when (original is OperationCanceledException
                                                  or IOException
                                                  or HttpRequestException
                                                  or GameSaveApiException
                                                  or InvalidDataException)
        {
            CloudSyncResult? reconciled = await TryReconcileAmbiguousCommitAsync(
                server, deviceToken, userId, gameId, serverKey, manifest,
                rootDescriptors, missing.Count, startedAt);
            if (reconciled is not null) return reconciled;
            throw;
        }

        await localSyncStateStore.SaveAsync(
            new LocalSyncState(serverKey, gameId, committed.SnapshotId, committed.HeadVersion, userId),
            CancellationToken.None);

        progress?.Report(new CloudSyncProgress("完成", missing.Count, missing.Count, "同步完成。"));
        string message = committed.Created
            ? $"同步成功：上传 {missing.Count} 个新内容对象，创建快照 {committed.SnapshotId}。"
            : $"同步完成：存档内容没有变化，继续使用快照 {committed.SnapshotId}，未创建重复版本。";
        return new CloudSyncResult(
            CloudSyncStatus.Success,
            message,
            committed.SnapshotId,
            uploaded,
            committed.FileCount,
            committed.LogicalSize,
            Stopwatch.GetElapsedTime(startedAt),
            committed.SnapshotId,
            removedFileCount);
    }

    private static bool HeadsMatch(LocalSyncState? localState, CloudHead remoteHead)
    {
        if (localState is null)
        {
            return remoteHead.HeadSnapshotId is null;
        }
        return string.Equals(
            localState.HeadSnapshotId,
            remoteHead.HeadSnapshotId,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// 把 Manifest 相对路径解析回本地文件，并再次确认结果仍位于存档根目录内。
    /// 使用 Path.GetRelativePath 做边界判断，避免磁盘根目录被 TrimEnd 成 C: 后改变 Path.Combine 语义。
    /// </summary>
    private static string ResolveSourcePath(IReadOnlyList<SaveRootRule> roots, string manifestPath)
    {
        int separator = manifestPath.IndexOf('/');
        if (separator <= 0 || separator == manifestPath.Length - 1) throw new IOException($"快照路径缺少根目录标识: {manifestPath}");
        string rootId = manifestPath[..separator];
        SaveRootRule? rule = roots.FirstOrDefault(root => string.Equals(root.RootId, rootId, StringComparison.OrdinalIgnoreCase));
        if (rule is null) throw new IOException($"快照路径引用了未知存档根目录: {rootId}");
        string root = Path.GetFullPath(rule.Path);
        string fullPath = Path.GetFullPath(Path.Combine(root, manifestPath[(separator + 1)..].Replace('/', Path.DirectorySeparatorChar)));
        string relative = Path.GetRelativePath(root, fullPath);
        if (Path.IsPathRooted(relative) || relative.Equals("..", StringComparison.Ordinal) || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)) throw new IOException($"快照路径越过存档根目录边界: {manifestPath}");
        return fullPath;
    }

    public async Task<CloudFreshnessResult> CheckFreshnessAsync(
        Uri server,
        string deviceToken,
        string userId,
        string gameId,
        IReadOnlyList<SaveRootRule> saveRoots,
        CancellationToken cancellationToken)
    {
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalSyncState? localState = await localSyncStateStore.GetAsync(serverKey, userId, gameId, cancellationToken);
        CloudHead remoteHead = await apiClient.GetHeadAsync(server, deviceToken, gameId, cancellationToken);
        ValidateHead(remoteHead, gameId);
        IReadOnlyList<SnapshotFile> currentLocalFiles = await manifestBuilder.BuildAsync(saveRoots, cancellationToken);
        if (HeadsMatch(localState, remoteHead))
        {
            if (remoteHead.HeadSnapshotId is null)
                return new CloudFreshnessResult(
                    currentLocalFiles.Count == 0 ? CloudFreshnessStatus.UpToDate : CloudFreshnessStatus.LocalAhead,
                    null,
                    localState?.HeadSnapshotId,
                    currentLocalFiles.Count,
                    currentLocalFiles.Sum(file => file.Size));
            CloudSnapshotManifest currentBaseline = await apiClient.GetSnapshotAsync(
                server, deviceToken, gameId, remoteHead.HeadSnapshotId, cancellationToken);
            ValidateCloudManifest(currentBaseline, gameId, remoteHead.HeadSnapshotId);
            if (localState is not null && localState.HeadVersion != remoteHead.Version)
            {
                await localSyncStateStore.SaveAsync(
                    new LocalSyncState(
                        serverKey, gameId, remoteHead.HeadSnapshotId, remoteHead.Version, userId),
                    CancellationToken.None);
            }
            if (ManifestsMatch(currentLocalFiles, currentBaseline.Files))
                return new CloudFreshnessResult(
                    CloudFreshnessStatus.UpToDate,
                    remoteHead.HeadSnapshotId,
                    localState?.HeadSnapshotId,
                    currentLocalFiles.Count,
                    currentLocalFiles.Sum(file => file.Size));
            int removed = CountRemovedFiles(currentBaseline.Files, currentLocalFiles);
            return new CloudFreshnessResult(
                IsDestructiveChange(currentBaseline.Files.Count, currentLocalFiles.Count, removed)
                    ? CloudFreshnessStatus.LocalDataMissing
                    : CloudFreshnessStatus.LocalAhead,
                remoteHead.HeadSnapshotId,
                localState?.HeadSnapshotId,
                currentLocalFiles.Count,
                currentLocalFiles.Sum(file => file.Size));
        }
        if (localState?.HeadSnapshotId is null && remoteHead.HeadSnapshotId is not null)
        {
            CloudSnapshotManifest remoteBaseline = await apiClient.GetSnapshotAsync(
                server, deviceToken, gameId, remoteHead.HeadSnapshotId, cancellationToken);
            ValidateCloudManifest(remoteBaseline, gameId, remoteHead.HeadSnapshotId);
            if (ManifestsMatch(currentLocalFiles, remoteBaseline.Files))
            {
                await localSyncStateStore.SaveAsync(
                    new LocalSyncState(serverKey, gameId, remoteHead.HeadSnapshotId,
                        remoteHead.Version, userId),
                    CancellationToken.None);
                return new CloudFreshnessResult(
                    CloudFreshnessStatus.UpToDate,
                    remoteHead.HeadSnapshotId,
                    remoteHead.HeadSnapshotId,
                    currentLocalFiles.Count,
                    currentLocalFiles.Sum(file => file.Size));
            }
            return new CloudFreshnessResult(
                currentLocalFiles.Count == 0 ? CloudFreshnessStatus.RemoteAheadLocalUnchanged : CloudFreshnessStatus.BaselineMissing,
                remoteHead.HeadSnapshotId,
                null,
                currentLocalFiles.Count,
                currentLocalFiles.Sum(file => file.Size));
        }
        if (localState?.HeadSnapshotId is null || remoteHead.HeadSnapshotId is null)
            return new CloudFreshnessResult(
                CloudFreshnessStatus.BaselineMissing,
                remoteHead.HeadSnapshotId,
                localState?.HeadSnapshotId,
                currentLocalFiles.Count,
                currentLocalFiles.Sum(file => file.Size));

        if (localState.HeadVersion == LocalSyncState.IntentionalRestorePendingVersion)
        {
            CloudSnapshotManifest pendingRemote = await apiClient.GetSnapshotAsync(
                server, deviceToken, gameId, remoteHead.HeadSnapshotId, cancellationToken);
            ValidateCloudManifest(pendingRemote, gameId, remoteHead.HeadSnapshotId);
            if (ManifestsMatch(currentLocalFiles, pendingRemote.Files))
            {
                await localSyncStateStore.SaveAsync(
                    new LocalSyncState(
                        serverKey, gameId, remoteHead.HeadSnapshotId, remoteHead.Version, userId),
                    CancellationToken.None);
                return new CloudFreshnessResult(
                    CloudFreshnessStatus.UpToDate,
                    remoteHead.HeadSnapshotId,
                    remoteHead.HeadSnapshotId,
                    currentLocalFiles.Count,
                    currentLocalFiles.Sum(file => file.Size));
            }
            return new CloudFreshnessResult(
                CloudFreshnessStatus.Diverged,
                remoteHead.HeadSnapshotId,
                localState.HeadSnapshotId,
                currentLocalFiles.Count,
                currentLocalFiles.Sum(file => file.Size));
        }

        CloudSnapshotManifest baseline = await apiClient.GetSnapshotAsync(
            server, deviceToken, gameId, localState.HeadSnapshotId, cancellationToken);
        ValidateCloudManifest(baseline, gameId, localState.HeadSnapshotId);
        bool localUnchanged = ManifestsMatch(currentLocalFiles, baseline.Files);
        return new CloudFreshnessResult(
            localUnchanged ? CloudFreshnessStatus.RemoteAheadLocalUnchanged : CloudFreshnessStatus.Diverged,
            remoteHead.HeadSnapshotId,
            localState.HeadSnapshotId,
            currentLocalFiles.Count,
            currentLocalFiles.Sum(file => file.Size));
    }

    private IReadOnlyList<SnapshotRootDescriptor> BuildRootDescriptors(IReadOnlyList<SaveRootRule> roots)
    {
        if (roots.Count > GameSaveProtocolLimits.MaximumSnapshotRoots)
            throw new InvalidOperationException($"单个快照最多支持 {GameSaveProtocolLimits.MaximumSnapshotRoots} 个存档根目录。");
        var descriptors = new List<SnapshotRootDescriptor>(roots.Count);
        foreach (SaveRootRule root in roots)
        {
            if (string.IsNullOrWhiteSpace(root.RootId)
                || root.RootId.Length > GameSaveProtocolLimits.RootIdMaxLength
                || root.RootId.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '_' and not '-'))
                throw new InvalidOperationException("存档根目录标识不符合云端协议要求。");
            if (root.Confidence is < 0 or > 100)
                throw new InvalidOperationException($"存档根目录 {root.RootId} 的置信度必须在 0 到 100 之间。");
            string pathTemplate = string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)
                ? "HKCU"
                : pathTemplateService.Encode(root.Path);
            if (string.IsNullOrWhiteSpace(pathTemplate)
                || pathTemplate.Length > GameSaveProtocolLimits.PathTemplateMaxLength
                || pathTemplate.Any(char.IsControl))
                throw new InvalidOperationException($"存档根目录 {root.RootId} 的可移植路径模板无效或过长。");
            descriptors.Add(new SnapshotRootDescriptor(
                root.RootId,
                string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase) ? "REGISTRY" : "FILE",
                pathTemplate,
                root.Source.ToString().ToUpperInvariant(),
                root.Confidence,
                NormalizePatterns(root.IncludePatterns, root.RootId),
                NormalizePatterns(root.ExcludePatterns, root.RootId)));
        }
        return descriptors;
    }

    private static IReadOnlyList<string> NormalizePatterns(
        IReadOnlyList<string> patterns,
        string rootId)
    {
        if (patterns.Count > GameSaveProtocolLimits.MaximumPatternsPerRoot)
            throw new InvalidOperationException($"存档根目录 {rootId} 的扫描规则数量超过协议上限。");
        return patterns.Select(pattern =>
        {
            string normalized = pattern?.Trim() ?? string.Empty;
            if (normalized.Length == 0
                || normalized.Length > GameSaveProtocolLimits.PatternMaxLength
                || normalized.Any(char.IsControl))
                throw new InvalidOperationException($"存档根目录 {rootId} 包含无效或过长的扫描规则。");
            return normalized;
        }).ToArray();
    }

    private static bool ManifestsMatch(IReadOnlyList<SnapshotFile> localFiles, IReadOnlyList<CloudSnapshotFile> cloudFiles)
    {
        if (localFiles.Count != cloudFiles.Count) return false;
        Dictionary<string, (string Hash, long Size)> cloud = cloudFiles.ToDictionary(
            file => file.RelativePath,
            file => (file.Sha256, file.Size),
            StringComparer.OrdinalIgnoreCase);
        return localFiles.All(file => cloud.TryGetValue(file.RelativePath, out var value)
            && value.Size == file.Size
            && string.Equals(value.Hash, file.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static bool LocalManifestsMatch(
        IReadOnlyList<SnapshotFile> first,
        IReadOnlyList<SnapshotFile> second)
    {
        if (first.Count != second.Count) return false;
        var expected = first.ToDictionary(
            file => file.RelativePath,
            file => (file.Sha256, file.Size),
            StringComparer.OrdinalIgnoreCase);
        return second.All(file => expected.TryGetValue(file.RelativePath, out var value)
            && value.Size == file.Size
            && string.Equals(value.Sha256, file.Sha256, StringComparison.OrdinalIgnoreCase));
    }

    private static bool RootDescriptorsMatch(
        IReadOnlyList<SnapshotRootDescriptor> expected,
        IReadOnlyList<CloudSnapshotRoot>? actual)
    {
        if (actual is null || actual.Count != expected.Count) return false;
        Dictionary<string, CloudSnapshotRoot> actualById = actual.ToDictionary(
            root => root.RootId,
            StringComparer.OrdinalIgnoreCase);
        foreach (SnapshotRootDescriptor descriptor in expected)
        {
            if (!actualById.TryGetValue(descriptor.RootId, out CloudSnapshotRoot? root)
                || !string.Equals(root.RootType, descriptor.RootType, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(root.PathTemplate, descriptor.PathTemplate, StringComparison.Ordinal)
                || !string.Equals(root.Source, descriptor.Source, StringComparison.OrdinalIgnoreCase)
                || root.Confidence != descriptor.Confidence
                || !(root.IncludePatterns ?? []).SequenceEqual(descriptor.IncludePatterns, StringComparer.Ordinal)
                || !(root.ExcludePatterns ?? []).SequenceEqual(descriptor.ExcludePatterns, StringComparer.Ordinal))
                return false;
        }
        return true;
    }

    private static void ValidateMissingObjects(
        IReadOnlyList<ContentObjectDescriptor> requested,
        IReadOnlyList<ContentObjectDescriptor> missing)
    {
        if (missing.Count > requested.Count)
            throw new InvalidDataException("服务端返回的缺失对象数量超过本次请求。");
        HashSet<string> expected = requested
            .Select(DescriptorKey)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var observed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ContentObjectDescriptor descriptor in missing)
        {
            if (descriptor is null || descriptor.Size < 0
                || string.IsNullOrWhiteSpace(descriptor.Sha256)
                || descriptor.Sha256.Length != 64
                || descriptor.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("服务端返回了无效的缺失对象描述。");
            string key = DescriptorKey(descriptor);
            if (!expected.Contains(key) || !observed.Add(key))
                throw new InvalidDataException("服务端返回了本次 Manifest 中不存在或重复的缺失对象。");
        }
    }

    private static string DescriptorKey(ContentObjectDescriptor descriptor) =>
        $"{descriptor.Sha256}:{descriptor.Size}";

    private static void ValidateCommitResult(
        CloudSnapshotResult committed,
        IReadOnlyList<SnapshotFile> manifest,
        CloudHead previousHead)
    {
        long logicalSize = manifest.Sum(file => file.Size);
        long expectedVersion = committed.Created
            ? checked(previousHead.Version + 1)
            : previousHead.Version;
        if (string.IsNullOrWhiteSpace(committed.SnapshotId)
            || committed.SnapshotId.Length > 256
            || committed.HeadVersion != expectedVersion
            || committed.FileCount != manifest.Count
            || committed.LogicalSize != logicalSize
            || committed.ChangedFileCount is < 0 or > GameSaveProtocolLimits.MaximumManifestFiles * 2
            || committed.Created && string.Equals(
                committed.SnapshotId, previousHead.HeadSnapshotId, StringComparison.Ordinal)
            || !committed.Created && (previousHead.HeadSnapshotId is null
                                      || committed.ChangedFileCount != 0
                                      || !string.Equals(committed.SnapshotId,
                                          previousHead.HeadSnapshotId, StringComparison.Ordinal)))
            throw new InvalidDataException("服务端返回的快照提交结果与本机 Manifest 不一致。");
    }

    private async Task<bool> TryRepairMatchingRemoteHeadAsync(
        Uri server,
        string deviceToken,
        string userId,
        string gameId,
        string serverKey,
        CloudHead remoteHead,
        IReadOnlyList<SnapshotFile> localManifest,
        CancellationToken cancellationToken)
    {
        if (remoteHead.HeadSnapshotId is null) return false;
        CloudSnapshotManifest remoteManifest = await apiClient.GetSnapshotAsync(
            server, deviceToken, gameId, remoteHead.HeadSnapshotId, cancellationToken);
        ValidateCloudManifest(remoteManifest, gameId, remoteHead.HeadSnapshotId);
        if (!ManifestsMatch(localManifest, remoteManifest.Files)) return false;
        await localSyncStateStore.SaveAsync(
            new LocalSyncState(serverKey, gameId, remoteHead.HeadSnapshotId, remoteHead.Version, userId),
            CancellationToken.None);
        return true;
    }

    private async Task<CloudSyncResult?> TryReconcileAmbiguousCommitAsync(
        Uri server,
        string deviceToken,
        string userId,
        string gameId,
        string serverKey,
        IReadOnlyList<SnapshotFile> localManifest,
        IReadOnlyList<SnapshotRootDescriptor> expectedRoots,
        int uploadedCount,
        long startedAt)
    {
        try
        {
            CloudHead head = await apiClient.GetHeadAsync(server, deviceToken, gameId, CancellationToken.None);
            ValidateHead(head, gameId);
            if (head.HeadSnapshotId is null) return null;
            CloudSnapshotManifest remote = await apiClient.GetSnapshotAsync(
                server, deviceToken, gameId, head.HeadSnapshotId, CancellationToken.None);
            ValidateCloudManifest(remote, gameId, head.HeadSnapshotId);
            if (!ManifestsMatch(localManifest, remote.Files)
                || !RootDescriptorsMatch(expectedRoots, remote.Roots)) return null;
            await localSyncStateStore.SaveAsync(
                new LocalSyncState(serverKey, gameId, head.HeadSnapshotId, head.Version, userId),
                CancellationToken.None);
            return new CloudSyncResult(
                CloudSyncStatus.Success,
                "服务端已提交快照；客户端已重新核对云端 HEAD 并修复本机同步状态。",
                head.HeadSnapshotId,
                uploadedCount,
                localManifest.Count,
                localManifest.Sum(file => file.Size),
                Stopwatch.GetElapsedTime(startedAt),
                head.HeadSnapshotId);
        }
        catch
        {
            return null;
        }
    }

    private static int CountRemovedFiles(
        IReadOnlyList<CloudSnapshotFile> baseline,
        IReadOnlyList<SnapshotFile> current)
    {
        var currentPaths = current.Select(file => file.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return baseline.Count(file => !currentPaths.Contains(file.RelativePath));
    }

    internal static bool IsDestructiveChange(int baselineCount, int currentCount, int removedCount)
    {
        if (baselineCount <= 0 || removedCount <= 0) return false;
        if (currentCount == 0) return true;
        bool atLeastHalfRemoved = removedCount * 2 >= baselineCount;
        return atLeastHalfRemoved && (removedCount >= 3 || baselineCount <= 4);
    }

    private static void ValidateHead(CloudHead head, string expectedGameId)
    {
        if (!string.Equals(head.GameId, expectedGameId, StringComparison.Ordinal)
            || head.Version < 0
            || head.HeadSnapshotId is { } snapshotId
               && (string.IsNullOrWhiteSpace(snapshotId) || snapshotId.Length > 256))
            throw new InvalidDataException("服务端返回的 HEAD 与同步游戏不一致。");
    }

    private static void ValidateCloudManifest(
        CloudSnapshotManifest manifest,
        string expectedGameId,
        string expectedSnapshotId)
    {
        CloudApiResponseValidator.ValidateManifest(manifest, expectedGameId, expectedSnapshotId);
        if (!string.Equals(manifest.GameId, expectedGameId, StringComparison.Ordinal)
            || !string.Equals(manifest.SnapshotId, expectedSnapshotId, StringComparison.Ordinal)
            || manifest.Files is null)
            throw new InvalidDataException("服务端返回的快照身份与同步请求不一致。");
        if (manifest.Files.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            throw new InvalidDataException("服务端快照超过客户端允许的文件数量上限。");
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudSnapshotFile file in manifest.Files)
        {
            if (file is null
                || string.IsNullOrWhiteSpace(file.RelativePath)
                || file.RelativePath.Length > GameSaveProtocolLimits.RelativePathMaxLength
                || Path.IsPathRooted(file.RelativePath)
                || file.RelativePath.Replace('\\', '/').Split('/').Any(part => part == "..")
                || !paths.Add(file.RelativePath.Replace('\\', '/'))
                || string.IsNullOrWhiteSpace(file.ObjectId)
                || file.Size < 0
                || string.IsNullOrWhiteSpace(file.Sha256)
                || file.Sha256.Length != 64
                || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException("服务端快照包含无效或重复的文件描述。");
        }
        if (manifest.Roots is not { } roots) return;
        if (roots.Count > GameSaveProtocolLimits.MaximumSnapshotRoots)
            throw new InvalidDataException("服务端快照超过客户端允许的根目录数量上限。");
        var rootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudSnapshotRoot root in roots)
        {
            if (root is null || string.IsNullOrWhiteSpace(root.RootId)
                || root.RootId.Length > GameSaveProtocolLimits.RootIdMaxLength
                || root.RootId.Contains('/') || root.RootId.Contains('\\')
                || !rootIds.Add(root.RootId))
                throw new InvalidDataException("服务端快照包含无效或重复的根目录描述。");
        }
    }
}

public sealed class DestructiveSnapshotChangeException(
    int baselineFileCount,
    int currentFileCount,
    int removedFileCount)
    : InvalidOperationException(
        $"检测到高风险存档变化：上次云端版本有 {baselineFileCount} 个文件，本次仅有 {currentFileCount} 个，缺少 {removedFileCount} 个。已阻止提交。")
{
    public int BaselineFileCount { get; } = baselineFileCount;
    public int CurrentFileCount { get; } = currentFileCount;
    public int RemovedFileCount { get; } = removedFileCount;
}
