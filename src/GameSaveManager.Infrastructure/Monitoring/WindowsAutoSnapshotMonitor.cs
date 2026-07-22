using System.Diagnostics;
using GameSaveManager.Application.Monitoring;

namespace GameSaveManager.Infrastructure.Monitoring;

/// <summary>使用 dirty 版本号和退避重试，确保失败或同步期间的新变化不会被误清理。</summary>
public sealed class WindowsAutoSnapshotMonitor : IAutoSnapshotMonitor
{
    private readonly SemaphoreSlim _callbackGate = new(1, 1);
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = [];
    private CancellationTokenSource? _lifetime;
    private Task? _loop;
    private AutoSnapshotProfile? _profile;
    private Func<CancellationToken, Task>? _onDirtyGameExitedAsync;
    private Func<CancellationToken, Task>? _onGameStartedAsync;
    private long _dirtyVersion;
    private long _cleanVersion;
    private volatile bool _wasRunning;
    private bool _syncPending;
    private int _consecutiveFailures;
    private DateTimeOffset _nextRetryAt;
    private DateTimeOffset _settleUntil;

    public bool IsRunning => _lifetime is { IsCancellationRequested: false };

    public Task StartAsync(
        AutoSnapshotProfile profile,
        Func<CancellationToken, Task> onDirtyGameExitedAsync,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? onGameStartedAsync = null)
    {
        ArgumentNullException.ThrowIfNull(profile.ProcessNames);
        if (profile.ProcessNames.Count == 0 || profile.ProcessNames.All(string.IsNullOrWhiteSpace)) throw new ArgumentException("至少需要一个游戏进程名。", nameof(profile));
        ArgumentNullException.ThrowIfNull(profile.SaveDirectories);
        ArgumentNullException.ThrowIfNull(onDirtyGameExitedAsync);
        return StartCoreAsync(profile, onDirtyGameExitedAsync, cancellationToken, onGameStartedAsync);
    }

    private async Task StartCoreAsync(
        AutoSnapshotProfile profile,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task>? startedCallback)
    {
        await StopAsync();
        cancellationToken.ThrowIfCancellationRequested();
        string[] directories = profile.SaveDirectories
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Where(Directory.Exists)
            .ToArray();
        if (directories.Length == 0 && !profile.SyncOnEveryGameExit)
            throw new DirectoryNotFoundException("自动快照没有可监听的存档目录。");

        var created = new List<FileSystemWatcher>();
        try
        {
            foreach (string directory in directories)
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size
                };
                watcher.Changed += MarkDirty;
                watcher.Created += MarkDirty;
                watcher.Deleted += MarkDirty;
                watcher.Renamed += MarkDirty;
                watcher.Error += MarkDirtyOnWatcherError;
                watcher.EnableRaisingEvents = true;
                created.Add(watcher);
            }
        }
        catch
        {
            foreach (FileSystemWatcher watcher in created) watcher.Dispose();
            throw;
        }

        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        bool initiallyRunning = IsAnyProcessRunning(profile.ProcessNames);
        lock (_gate)
        {
            _profile = profile with { SaveDirectories = directories };
            _onDirtyGameExitedAsync = callback;
            _onGameStartedAsync = startedCallback;
            _dirtyVersion = 0;
            _cleanVersion = 0;
            _wasRunning = initiallyRunning;
            _syncPending = false;
            _consecutiveFailures = 0;
            _nextRetryAt = DateTimeOffset.MinValue;
            _settleUntil = DateTimeOffset.MinValue;
            _watchers.AddRange(created);
            _lifetime = lifetime;
            _loop = MonitorLoopAsync(lifetime.Token);
        }
        if (initiallyRunning && startedCallback is not null)
            await startedCallback(lifetime.Token);
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
            _onGameStartedAsync = null;
            _dirtyVersion = 0;
            _cleanVersion = 0;
            _wasRunning = false;
            _syncPending = false;
            _consecutiveFailures = 0;
            _nextRetryAt = DateTimeOffset.MinValue;
            _settleUntil = DateTimeOffset.MinValue;
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
            if (running)
            {
                bool justStarted = !_wasRunning;
                _wasRunning = true;
                if (justStarted && _onGameStartedAsync is { } started)
                {
                    try { await started(cancellationToken); }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                    catch (Exception exception)
                    {
                        Trace.TraceWarning($"游戏启动前置检查失败：{exception.GetType().Name}");
                    }
                }
                continue;
            }
            if (_wasRunning)
            {
                _wasRunning = false;
                lock (_gate)
                {
                    // 进程退出是最终可信边界。即使 FileSystemWatcher 在客户端启动前、
                    // 缓冲区溢出或游戏原子替换文件时漏掉事件，也必须做一次增量核对；
                    // 内容未变化时云端提交为幂等 no-op，不会制造重复快照。
                    _syncPending = true;
                    _settleUntil = DateTimeOffset.UtcNow.AddSeconds(5);
                }
            }
            bool syncPending;
            DateTimeOffset nextRetryAt;
            DateTimeOffset settleUntil;
            lock (_gate)
            {
                syncPending = _syncPending;
                nextRetryAt = _nextRetryAt;
                settleUntil = _settleUntil;
            }
            if (!syncPending || DateTimeOffset.UtcNow < nextRetryAt || DateTimeOffset.UtcNow < settleUntil) continue;
            long versionBeingSynced = Volatile.Read(ref _dirtyVersion);
            await _callbackGate.WaitAsync(cancellationToken);
            try
            {
                await callback(cancellationToken);
                Volatile.Write(ref _cleanVersion, versionBeingSynced);
                lock (_gate)
                {
                    _consecutiveFailures = 0;
                    _nextRetryAt = DateTimeOffset.MinValue;
                    _syncPending = Volatile.Read(ref _dirtyVersion) > versionBeingSynced;
                    if (_syncPending) _settleUntil = DateTimeOffset.UtcNow.AddSeconds(5);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                DateTimeOffset retryAt;
                lock (_gate)
                {
                    _consecutiveFailures++;
                    _syncPending = true;
                    _nextRetryAt = DateTimeOffset.UtcNow + GetRetryDelay(_consecutiveFailures);
                    retryAt = _nextRetryAt;
                }
                Trace.TraceWarning($"自动同步失败，将在 {retryAt:O} 后重试：{exception.GetType().Name}");
            }
            finally { _callbackGate.Release(); }
        }
    }

    private void MarkDirty(object? sender, EventArgs eventArgs)
    {
        Interlocked.Increment(ref _dirtyVersion);
        lock (_gate)
        {
            _syncPending = true;
            _settleUntil = DateTimeOffset.UtcNow.AddSeconds(5);
        }
    }
    private void MarkDirtyOnWatcherError(object? sender, ErrorEventArgs eventArgs)
    {
        Trace.TraceWarning($"自动同步目录监控发生错误：{eventArgs.GetException().GetType().Name}");
        MarkDirty(sender, eventArgs);
    }

    internal static TimeSpan GetRetryDelay(int failureCount) => failureCount switch
    {
        <= 1 => TimeSpan.FromSeconds(30),
        2 => TimeSpan.FromMinutes(2),
        _ => TimeSpan.FromMinutes(10)
    };

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
