namespace GameSaveManager.Application.Monitoring;

/// <summary>
/// 将游戏进程状态和目录变更组合为“游戏退出后可安全创建快照”的信号。
/// 目录事件只能标记 dirty，真正触发必须由进程退出确认。
/// </summary>
public interface IAutoSnapshotMonitor : IAsyncDisposable
{
    bool IsRunning { get; }

    Task StartAsync(
        AutoSnapshotProfile profile,
        Func<CancellationToken, Task> onDirtyGameExitedAsync,
        CancellationToken cancellationToken);

    Task StopAsync();
}