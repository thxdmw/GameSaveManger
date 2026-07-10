using GameSaveManager.Application.Api;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Application.Sync;

/// <summary>
/// 客户端主动同步编排。
/// 本地确认 HEAD 与远端 HEAD 不一致时立即阻断，禁止把落后设备的存档静默接到最新云端版本之后。
/// </summary>
public sealed class CloudSyncService(
    SaveManifestBuilder manifestBuilder,
    IGameSaveApiClient apiClient,
    ILocalSyncStateStore localSyncStateStore)
{
    public async Task<CloudSyncResult> SyncAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string saveDirectory,
        SnapshotTrigger trigger,
        string? description,
        CancellationToken cancellationToken)
    {
        LocalSyncState? localState = await localSyncStateStore.GetAsync(gameId, cancellationToken);
        CloudHead remoteHead = await apiClient.GetHeadAsync(server, deviceToken, gameId, cancellationToken);

        if (!HeadsMatch(localState, remoteHead))
        {
            return new CloudSyncResult(
                CloudSyncStatus.RemoteAhead,
                "云端 HEAD 与本机最后确认版本不一致，已阻止上传；需要先进入冲突处理或恢复流程。",
                null,
                0,
                0,
                0);
        }

        IReadOnlyList<SnapshotFile> manifest =
            await manifestBuilder.BuildAsync(saveDirectory, cancellationToken);

        IReadOnlyList<ContentObjectDescriptor> objectDescriptors = manifest
            .Select(file => new ContentObjectDescriptor(file.Sha256, file.Size))
            .Distinct()
            .ToArray();
        IReadOnlyList<ContentObjectDescriptor> missing = await apiClient.CheckMissingAsync(
            server, deviceToken, objectDescriptors, cancellationToken);

        Dictionary<ContentObjectDescriptor, SnapshotFile> sourceFiles = manifest
            .GroupBy(file => new ContentObjectDescriptor(file.Sha256, file.Size))
            .ToDictionary(group => group.Key, group => group.First());
        foreach (ContentObjectDescriptor descriptor in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!sourceFiles.TryGetValue(descriptor, out SnapshotFile? source))
            {
                throw new InvalidOperationException("服务端返回了本次 Manifest 中不存在的缺失对象");
            }

            string fullPath = ResolveSourcePath(saveDirectory, source.RelativePath);
            await apiClient.UploadObjectAsync(
                server, deviceToken, fullPath, descriptor, cancellationToken);
        }

        CloudSnapshotResult committed = await apiClient.CommitSnapshotAsync(
            server,
            deviceToken,
            gameId,
            remoteHead.HeadSnapshotId,
            trigger,
            description,
            manifest,
            cancellationToken);

        await localSyncStateStore.SaveAsync(
            new LocalSyncState(gameId, committed.SnapshotId, committed.HeadVersion),
            cancellationToken);

        return new CloudSyncResult(
            CloudSyncStatus.Success,
            $"同步成功：上传 {missing.Count} 个新内容对象，创建快照 {committed.SnapshotId}。",
            committed.SnapshotId,
            missing.Count,
            committed.FileCount,
            committed.LogicalSize);
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

    private static string ResolveSourcePath(string saveDirectory, string relativePath)
    {
        string root = Path.GetFullPath(saveDirectory)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string localRelativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);
        string fullPath = Path.GetFullPath(Path.Combine(root, localRelativePath));
        string rootPrefix = root + Path.DirectorySeparatorChar;
        if (!fullPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new IOException($"快照相对路径越过存档目录边界: {relativePath}");
        }
        return fullPath;
    }
}
