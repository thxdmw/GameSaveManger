using System.Diagnostics;
using GameSaveManager.Application.Monitoring;

namespace GameSaveManager.Infrastructure.Monitoring;

/// <summary>
/// Windows 自动快照监控器：FileSystemWatcher 只设置 dirty 标记，
/// 轮询游戏进程退出后才调用上层同步回调。轮询用于弥补 WMI/目录事件可能丢失的情况。
/// </summary>
public sealed class WindowsAutoSnapshotMonitor : IAutoSnapshotMonitor
{
    private readonly SemaphoreSlim _callbackGate = new(1, 1);
    private readonly object _gate = new();
    private CancellationTokenSource? _lifetime;
    private FileSystemWatcher? _watcher;
    private Task? _loop;
    private AutoSnapshotProfile? _profile;
    private Func<CancellationToken, Task>? _onDirtyGameExitedAsync;
    private volatile bool _dirty;
    private volatile bool _wasRunning;

    public bool IsRunning => _lifetime is { IsCancellationRequested: false };

    public Task StartAsync(
        AutoSnapshotProfile profile,
        Func<CancellationToken, Task> onDirtyGameExitedAsync,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.ProcessName);
        ArgumentException.ThrowIfNullOrWhiteSpace(profile.SaveDirectory);
        ArgumentNullException.ThrowIfNull(onDirtyGameExitedAsync);
        if (!Directory.Exists(profile.SaveDirectory))
        {
            throw new DirectoryNotFoundException($"自动快照的存档目录不存在: {profile.SaveDirectory}");
        }

        return StartCoreAsync(profile, onDirtyGameExitedAsync, cancellationToken);
    }

    private async Task StartCoreAsync(
        AutoSnapshotProfile profile,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken)
    {
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var watcher = new FileSystemWatcher(profile.SaveDirectory)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
            EnableRaisingEvents = true
        };
        watcher.Changed += MarkDirty;
        watcher.Created += MarkDirty;
        watcher.Deleted += MarkDirty;
        watcher.Renamed += MarkDirty;
        watcher.Error += MarkDirty;

        lock (_gate)
        {
            _profile = profile;
            _onDirtyGameExitedAsync = callback;
            _dirty = false;
            _wasRunning = IsProcessRunning(profile.ProcessName);
            _watcher = watcher;
            _lifetime = lifetime;
            _loop = MonitorLoopAsync(lifetime.Token);
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetime;
        Task? loop;
        FileSystemWatcher? watcher;
        lock (_gate)
        {
            lifetime = _lifetime;
            loop = _loop;
            watcher = _watcher;
            _lifetime = null;
            _loop = null;
            _watcher = null;
            _profile = null;
            _onDirtyGameExitedAsync = null;
            _dirty = false;
            _wasRunning = false;
        }

        if (lifetime is null)
        {
            return;
        }
        lifetime.Cancel();
        watcher?.Dispose();
        try
        {
            if (loop is not null)
            {
                await loop;
            }
        }
        catch (OperationCanceledException)
        {
            // 停止监控属于正常控制流。
        }
        finally
        {
            lifetime.Dispose();
        }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            AutoSnapshotProfile? profile = _profile;
            Func<CancellationToken, Task>? callback = _onDirtyGameExitedAsync;
            if (profile is null || callback is null)
            {
                continue;
            }

            bool running = IsProcessRunning(profile.ProcessName);
            if (running)
            {
                _wasRunning = true;
                continue;
            }
            if (!_wasRunning || !_dirty)
            {
                continue;
            }

            _wasRunning = false;
            _dirty = false;
            await _callbackGate.WaitAsync(cancellationToken);
            try
            {
                await callback(cancellationToken);
            }
            finally
            {
                _callbackGate.Release();
            }
        }
    }

    private void MarkDirty(object? sender, EventArgs eventArgs) => _dirty = true;

    private static bool IsProcessRunning(string processName)
    {
        string normalized = Path.GetFileNameWithoutExtension(processName.Trim());
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }
        Process[] processes = Process.GetProcessesByName(normalized);
        try
        {
            return processes.Length > 0;
        }
        finally
        {
            foreach (Process process in processes)
            {
                process.Dispose();
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _callbackGate.Dispose();
    }
}