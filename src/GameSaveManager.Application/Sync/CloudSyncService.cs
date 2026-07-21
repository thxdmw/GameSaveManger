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

    public Task<CloudSyncResult> SyncAsync(Uri server, string deviceToken, string userId, string gameId, string saveDirectory, SnapshotTrigger trigger, string? description, CancellationToken cancellationToken, bool keepLocalOnConflict = false, IProgress<CloudSyncProgress>? progress = null) =>
        SyncCoreAsync(server, deviceToken, userId, gameId, [SaveRootRule.CreateDefault(saveDirectory, Discovery.SaveLocationSource.Manual, 100, true)], trigger, description, cancellationToken, keepLocalOnConflict, progress);

    public Task<CloudSyncResult> SyncAsync(Uri server, string deviceToken, string userId, string gameId, IReadOnlyList<SaveRootRule> saveRoots, SnapshotTrigger trigger, string? description, CancellationToken cancellationToken, bool keepLocalOnConflict = false, IProgress<CloudSyncProgress>? progress = null) =>
        SyncCoreAsync(server, deviceToken, userId, gameId, saveRoots, trigger, description, cancellationToken, keepLocalOnConflict, progress);

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
        IProgress<CloudSyncProgress>? progress = null)
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

        if (!HeadsMatch(localState, remoteHead) && !keepLocalOnConflict)
        {
            return new CloudSyncResult(
                CloudSyncStatus.RemoteAhead,
                "云端 HEAD 与本机最后确认版本不一致，已阻止上传；需要先进入冲突处理或恢复流程。",
                null,
                0,
                0,
                0,
                Stopwatch.GetElapsedTime(startedAt));
        }

        // 用户显式选择“保留本机版本”时，以当前云端 HEAD 为父快照提交；旧云端版本保留在时间线中。
        progress?.Report(new CloudSyncProgress("扫描", 0, 0, "正在扫描存档并计算内容 Hash…"));
        IReadOnlyList<SnapshotFile> manifest =
            await manifestBuilder.BuildAsync(saveRoots, cancellationToken);
        if (manifest.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            throw new InvalidOperationException(GameSaveProtocolLimits.ManifestFileLimitMessage);

        IReadOnlyList<ContentObjectDescriptor> objectDescriptors = manifest
            .Select(file => new ContentObjectDescriptor(file.Sha256, file.Size))
            .Distinct()
            .ToArray();
        progress?.Report(new CloudSyncProgress("比对", 0, objectDescriptors.Count, "正在检查云端缺失内容…"));
        IReadOnlyList<ContentObjectDescriptor> missing = await apiClient.CheckMissingAsync(
            server, deviceToken, objectDescriptors, cancellationToken);

        Dictionary<ContentObjectDescriptor, SnapshotFile> sourceFiles = manifest
            .GroupBy(file => new ContentObjectDescriptor(file.Sha256, file.Size))
            .ToDictionary(group => group.Key, group => group.First());
        int uploaded = 0;
        foreach (ContentObjectDescriptor descriptor in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sourceFiles.TryGetValue(descriptor, out SnapshotFile? source))
            {
                throw new InvalidOperationException("服务端返回了本次 Manifest 中不存在的缺失对象");
            }

            string fullPath = ResolveSourcePath(saveRoots, source.RelativePath);
            await apiClient.UploadObjectAsync(
                server, deviceToken, fullPath, descriptor, cancellationToken);
            uploaded++;
            progress?.Report(new CloudSyncProgress("上传", uploaded, missing.Count, $"正在上传缺失内容（{uploaded}/{missing.Count}）…"));
        }

        progress?.Report(new CloudSyncProgress("提交", missing.Count, missing.Count, "正在提交不可变快照…"));
        CloudSnapshotResult committed = await apiClient.CommitSnapshotAsync(
            server,
            deviceToken,
            gameId,
            remoteHead.HeadSnapshotId,
            trigger,
            description,
            BuildRootDescriptors(saveRoots),
            manifest,
            cancellationToken);

        await localSyncStateStore.SaveAsync(
            new LocalSyncState(serverKey, gameId, committed.SnapshotId, committed.HeadVersion, userId),
            cancellationToken);

        progress?.Report(new CloudSyncProgress("完成", missing.Count, missing.Count, "同步完成。"));
        string message = committed.Created
            ? $"同步成功：上传 {missing.Count} 个新内容对象，创建快照 {committed.SnapshotId}。"
            : $"同步完成：存档内容没有变化，继续使用快照 {committed.SnapshotId}，未创建重复版本。";
        return new CloudSyncResult(
            CloudSyncStatus.Success,
            message,
            committed.SnapshotId,
            missing.Count,
            committed.FileCount,
            committed.LogicalSize,
            Stopwatch.GetElapsedTime(startedAt));
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
        if (HeadsMatch(localState, remoteHead))
            return new CloudFreshnessResult(CloudFreshnessStatus.UpToDate, remoteHead.HeadSnapshotId, null);
        if (localState?.HeadSnapshotId is null && remoteHead.HeadSnapshotId is not null)
        {
            IReadOnlyList<SnapshotFile> untrackedLocalFiles = await manifestBuilder.BuildAsync(saveRoots, cancellationToken);
            return new CloudFreshnessResult(
                untrackedLocalFiles.Count == 0 ? CloudFreshnessStatus.RemoteAheadLocalUnchanged : CloudFreshnessStatus.BaselineMissing,
                remoteHead.HeadSnapshotId,
                null);
        }
        if (localState?.HeadSnapshotId is null || remoteHead.HeadSnapshotId is null)
            return new CloudFreshnessResult(CloudFreshnessStatus.BaselineMissing, remoteHead.HeadSnapshotId, localState?.HeadSnapshotId);

        IReadOnlyList<SnapshotFile> localFiles = await manifestBuilder.BuildAsync(saveRoots, cancellationToken);
        CloudSnapshotManifest baseline = await apiClient.GetSnapshotAsync(
            server, deviceToken, gameId, localState.HeadSnapshotId, cancellationToken);
        bool localUnchanged = ManifestsMatch(localFiles, baseline.Files);
        return new CloudFreshnessResult(
            localUnchanged ? CloudFreshnessStatus.RemoteAheadLocalUnchanged : CloudFreshnessStatus.Diverged,
            remoteHead.HeadSnapshotId,
            localState.HeadSnapshotId);
    }

    private IReadOnlyList<SnapshotRootDescriptor> BuildRootDescriptors(IReadOnlyList<SaveRootRule> roots) =>
        roots.Select(root => new SnapshotRootDescriptor(
            root.RootId,
            string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase) ? "REGISTRY" : "FILE",
            string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase) ? "HKCU" : pathTemplateService.Encode(root.Path),
            root.Source.ToString(),
            root.Confidence,
            root.IncludePatterns,
            root.ExcludePatterns)).ToArray();

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
}
