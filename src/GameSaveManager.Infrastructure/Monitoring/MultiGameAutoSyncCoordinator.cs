using GameSaveManager.Application.Monitoring;

namespace GameSaveManager.Infrastructure.Monitoring;

/// <summary>为每个游戏创建独立监控器，避免切换游戏时覆盖已启用的自动同步。</summary>
public sealed class MultiGameAutoSyncCoordinator : IAutoSyncCoordinator
{
    private readonly object _gate = new();
    private readonly SemaphoreSlim _changeGate = new(1, 1);
    private readonly Dictionary<string, WindowsAutoSnapshotMonitor> _monitors = new(StringComparer.Ordinal);

    public IReadOnlyCollection<string> ActiveGameIds
    {
        get { lock (_gate) return _monitors.Keys.ToArray(); }
    }

    public async Task EnableAsync(string gameId, AutoSnapshotProfile profile,
        Func<CancellationToken, Task> onDirtyGameExitedAsync, CancellationToken cancellationToken,
        Func<CancellationToken, Task>? onGameStartedAsync = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gameId);
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            var monitor = new WindowsAutoSnapshotMonitor();
            try
            {
                await monitor.StartAsync(profile, onDirtyGameExitedAsync, cancellationToken, onGameStartedAsync);
            }
            catch
            {
                await monitor.DisposeAsync();
                throw;
            }
            WindowsAutoSnapshotMonitor? previous;
            lock (_gate)
            {
                _monitors.TryGetValue(gameId, out previous);
                _monitors[gameId] = monitor;
            }
            if (previous is not null) await previous.DisposeAsync();
        }
        finally { _changeGate.Release(); }
    }

    public async Task DisableAsync(string gameId)
    {
        await _changeGate.WaitAsync();
        try
        {
            WindowsAutoSnapshotMonitor? monitor;
            lock (_gate)
            {
                _monitors.TryGetValue(gameId, out monitor);
                _monitors.Remove(gameId);
            }
            if (monitor is not null) await monitor.DisposeAsync();
        }
        finally { _changeGate.Release(); }
    }

    public async Task DisableAllAsync()
    {
        await _changeGate.WaitAsync();
        try
        {
            WindowsAutoSnapshotMonitor[] monitors;
            lock (_gate)
            {
                monitors = _monitors.Values.ToArray();
                _monitors.Clear();
            }
            foreach (WindowsAutoSnapshotMonitor monitor in monitors) await monitor.DisposeAsync();
        }
        finally { _changeGate.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        await DisableAllAsync();
        _changeGate.Dispose();
    }
}
