namespace GameSaveManager.Application.Sync;

/// <summary>本地同步 HEAD 持久化契约；同步状态必须按服务端与游戏双重隔离。</summary>
public interface ILocalSyncStateStore
{
    Task<LocalSyncState?> GetAsync(
        string serverKey,
        string gameId,
        CancellationToken cancellationToken);

    Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken);
}
