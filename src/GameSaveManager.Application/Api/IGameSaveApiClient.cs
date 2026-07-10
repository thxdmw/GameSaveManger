using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Application.Api;

/// <summary>GameSave 服务端 HTTP API 抽象，Application 层不依赖 HttpClient 具体实现。</summary>
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
}
