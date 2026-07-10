namespace GameSaveManager.Application.Sync;

/// <summary>本地同步 HEAD 持久化契约。</summary>
public interface ILocalSyncStateStore
{
    Task<LocalSyncState?> GetAsync(string gameId, CancellationToken cancellationToken);

    Task SaveAsync(LocalSyncState state, CancellationToken cancellationToken);
}
