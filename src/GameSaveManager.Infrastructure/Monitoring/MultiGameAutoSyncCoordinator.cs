using GameSaveManager.Application.Monitoring;

namespace GameSaveManager.Infrastructure.Monitoring;

/// <summary>为每个游戏创建独立监控器，避免切换游戏时覆盖已启用的自动同步。</summary>
public sealed class MultiGameAutoSyncCoordinator : IAutoSyncCoordinator
{
    private readonly object _gate = new();
    private readonly Dictionary<string, WindowsAutoSnapshotMonitor> _monitors = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ActiveGameIds
    {
        get { lock (_gate) return _monitors.Keys.ToArray(); }
    }

    public async Task EnableAsync(string gameId, AutoSnapshotProfile profile,
        Func<CancellationToken, Task> onDirtyGameExitedAsync, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        WindowsAutoSnapshotMonitor? previous;
        var monitor = new WindowsAutoSnapshotMonitor();
        lock (_gate)
        {
            _monitors.TryGetValue(gameId, out previous);
            _monitors[gameId] = monitor;
        }
        if (previous is not null) await previous.DisposeAsync();
        await monitor.StartAsync(profile, onDirtyGameExitedAsync, cancellationToken);
    }

    public async Task DisableAsync(string gameId)
    {
        WindowsAutoSnapshotMonitor? monitor;
        lock (_gate)
        {
            _monitors.TryGetValue(gameId, out monitor);
            _monitors.Remove(gameId);
        }
        if (monitor is not null) await monitor.DisposeAsync();
    }

    public async Task DisableAllAsync()
    {
        WindowsAutoSnapshotMonitor[] monitors;
        lock (_gate)
        {
            monitors = _monitors.Values.ToArray();
            _monitors.Clear();
        }
        foreach (WindowsAutoSnapshotMonitor monitor in monitors) await monitor.DisposeAsync();
    }

    public ValueTask DisposeAsync() => new(DisableAllAsync());
}