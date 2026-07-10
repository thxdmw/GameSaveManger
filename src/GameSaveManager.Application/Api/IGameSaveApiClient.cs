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
        CancellationToken cancellationToken);
}