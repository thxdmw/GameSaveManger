using System.Diagnostics;
using GameSaveManager.Application.Monitoring;

namespace GameSaveManager.Infrastructure.Monitoring;

/// <summary>所有目录 Watcher 只共用一个 dirty 标记，进程退出后由上层执行一次完整同步。</summary>
public sealed class WindowsAutoSnapshotMonitor : IAutoSnapshotMonitor
{
    private readonly SemaphoreSlim _callbackGate = new(1, 1);
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private AutoSnapshotProfile? _profile;
    private Func<CancellationToken, Task>? _onDirtyGameExitedAsync;
    private volatile bool _dirty;
    private volatile bool _wasRunning;

    public bool IsRunning => _lifetime is { IsCancellationRequested: false };

    public Task StartAsync(AutoSnapshotProfile profile, Func<CancellationToken, Task> onDirtyGameExitedAsync, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(profile.ProcessNames);
        if (profile.ProcessNames.Count == 0 || profile.ProcessNames.All(string.IsNullOrWhiteSpace)) throw new ArgumentException("至少需要一个游戏进程名。", nameof(profile));
        ArgumentNullException.ThrowIfNull(profile.SaveDirectories);
        ArgumentNullException.ThrowIfNull(onDirtyGameExitedAsync);
        return StartCoreAsync(profile, onDirtyGameExitedAsync, cancellationToken);
    }

    private async Task StartCoreAsync(AutoSnapshotProfile profile, Func<CancellationToken, Task> callback, CancellationToken cancellationToken)
    {
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();
        string[] directories = profile.SaveDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToArray();
        if (directories.Length == 0) throw new DirectoryNotFoundException("自动快照没有可监听的存档目录。");

        var created = new List<FileSystemWatcher>();
        try
        {
            foreach (string directory in directories)
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    EnableRaisingEvents = true
                };
                watcher.Changed += MarkDirty;
                watcher.Created += MarkDirty;
                watcher.Deleted += MarkDirty;
                watcher.Renamed += MarkDirty;
                watcher.Error += MarkDirtyOnWatcherError;
                created.Add(watcher);
            }
        }
        catch
        {
            foreach (FileSystemWatcher watcher in created) watcher.Dispose();
            throw;
        }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_gate)
        {
            _profile = profile with { SaveDirectories = directories };
            _onDirtyGameExitedAsync = callback;
            _dirty = false;
            _wasRunning = IsAnyProcessRunning(profile.ProcessNames);
            _watchers.AddRange(created);
            _lifetime = lifetime;
            _loop = MonitorLoopAsync(lifetime.Token);
        }
    }

    public async Task StopAsync()
    {
        CancellationTokenSource? lifetime;
        Task? loop;
        FileSystemWatcher[] watchers;
        lock (_gate)
        {
            lifetime = _lifetime;
            loop = _loop;
            watchers = _watchers.ToArray();
            _watchers.Clear();
            _lifetime = null;
            _loop = null;
            _profile = null;
            _onDirtyGameExitedAsync = null;
            _dirty = false;
            _wasRunning = false;
        }
        if (lifetime is null) return;
        lifetime.Cancel();
        foreach (FileSystemWatcher watcher in watchers) watcher.Dispose();
        try { if (loop is not null) await loop; }
        catch (OperationCanceledException) { }
        finally { lifetime.Dispose(); }
    }

    private async Task MonitorLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        while (await timer.WaitForNextTickAsync(cancellationToken))
        {
            AutoSnapshotProfile? profile = _profile;
            Func<CancellationToken, Task>? callback = _onDirtyGameExitedAsync;
            if (profile is null || callback is null) continue;
            bool running = IsAnyProcessRunning(profile.ProcessNames);
            if (running) { _wasRunning = true; continue; }
            if (!_wasRunning || !_dirty) continue;
            _wasRunning = false;
            _dirty = false;
            await _callbackGate.WaitAsync(cancellationToken);
            try { await callback(cancellationToken); }
            finally { _callbackGate.Release(); }
        }
    }

    private void MarkDirty(object? sender, EventArgs eventArgs) => _dirty = true;
    private void MarkDirtyOnWatcherError(object? sender, ErrorEventArgs eventArgs)
    {
        Trace.TraceWarning($"自动同步目录监控发生错误：{eventArgs.GetException().GetType().Name}");
        _dirty = true;
    }

    private static bool IsAnyProcessRunning(IReadOnlyList<string> processNames)
    {
        return processNames.Any(IsProcessRunning);
    }

    private static bool IsProcessRunning(string processName)
    {
        string normalized = Path.GetFileNameWithoutExtension(processName.Trim());
        if (string.IsNullOrWhiteSpace(normalized)) return false;
        Process[] processes = Process.GetProcessesByName(normalized);
        try { return processes.Length > 0; }
        finally { foreach (Process process in processes) process.Dispose(); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _callbackGate.Dispose();
    }
}
