namespace GameSaveManager.Application.Monitoring;

/// <summary>管理多个游戏的自动同步监控；每个游戏独立监听其进程和存档目录。</summary>
public interface IAutoSyncCoordinator : IAsyncDisposable
{
    IReadOnlyCollection<string> ActiveGameIds { get; }

    Task EnableAsync(
        string gameId,
        AutoSnapshotProfile profile,
        Func<CancellationToken, Task> onDirtyGameExitedAsync,
        CancellationToken cancellationToken);

    Task DisableAsync(string gameId);

    Task DisableAllAsync();
}