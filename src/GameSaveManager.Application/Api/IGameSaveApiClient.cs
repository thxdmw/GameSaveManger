using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Application.Api;

/// <summary>GameSave 服务端 HTTP API 抽象；Application 层不依赖 HttpClient 的具体实现。</summary>
public interface IGameSaveApiClient
{
    Task<AuthSession> RegisterAsync(
        Uri server,
        string username,
        string password,
        string deviceId,
        string deviceName,
        CancellationToken cancellationToken);

    Task<AuthSession> LoginAsync(
        Uri server,
        string username,
        string password,
        string deviceId,
        string deviceName,
        CancellationToken cancellationToken);

    /// <summary>使用已保存 Token 恢复稳定账号和设备身份。</summary>
    Task<CloudAccountSession> GetSessionAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken);

    /// <summary>读取单个云端游戏的快照保留策略。</summary>
    Task<CloudRetentionPolicy> GetRetentionPolicyAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken);

    /// <summary>更新单个云端游戏的快照保留策略。</summary>
    Task<CloudRetentionPolicy> UpdateRetentionPolicyAsync(
        Uri server,
        string deviceToken,
        string gameId,
        bool enabled,
        int retentionCount,
        int retentionDays,
        CancellationToken cancellationToken);

    /// <summary>立即执行一次保留策略。</summary>
    Task<CloudRetentionCleanupResult> CleanupRetentionAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken);
    /// <summary>读取当前账号的物理内容对象配额。</summary>
    Task<CloudQuota> GetQuotaAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken);
    /// <summary>读取当前账号的已登记设备。</summary>
    Task<IReadOnlyList<CloudDevice>> ListDevicesAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken);

    /// <summary>撤销其他设备；服务端拒绝撤销当前设备。</summary>
    Task RevokeDeviceAsync(
        Uri server,
        string deviceToken,
        string deviceId,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<CloudGame>> ListGamesAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken);

    Task<CloudGame> CreateGameAsync(
        Uri server,
        string deviceToken,
        string name,
        string provider,
        string? providerGameId,
        CancellationToken cancellationToken);

    /// <summary>删除云端游戏、全部快照，并释放不再被引用的内容对象。</summary>
    Task DeleteGameAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken);

    Task<CloudHead> GetHeadAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken);

    /// <summary>读取当前用户可见的快照时间线，按创建时间倒序返回。</summary>
    Task<IReadOnlyList<CloudSnapshotSummary>> ListSnapshotsAsync(
        Uri server,
        string deviceToken,
        string gameId,
        int limit,
        CancellationToken cancellationToken);

    /// <summary>删除非当前 HEAD 的历史快照；服务端会释放其内容对象引用并保留当前 HEAD。</summary>
    Task DeleteSnapshotAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<ContentObjectDescriptor>> CheckMissingAsync(
        Uri server,
        string deviceToken,
        IReadOnlyCollection<ContentObjectDescriptor> objects,
        CancellationToken cancellationToken);

    Task UploadObjectAsync(
        Uri server,
        string deviceToken,
        string filePath,
        ContentObjectDescriptor descriptor,
        CancellationToken cancellationToken);

    Task<CloudSnapshotResult> CommitSnapshotAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string? expectedHeadSnapshotId,
        SnapshotTrigger trigger,
        string? description,
        IReadOnlyList<SnapshotRootDescriptor> roots,
        IReadOnlyList<SnapshotFile> files,
        CancellationToken cancellationToken);

    /// <summary>读取当前用户拥有的快照 Manifest，服务端会校验快照与游戏的归属关系。</summary>
    Task<CloudSnapshotManifest> GetSnapshotAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        CancellationToken cancellationToken);

    /// <summary>将服务端授权后的对象内容下载到指定临时文件；调用方负责校验内容并原子移动到缓存。</summary>
    Task DownloadObjectAsync(
        Uri server,
        string deviceToken,
        string objectId,
        string destinationPath,
        long expectedSize,
        CancellationToken cancellationToken);
}
