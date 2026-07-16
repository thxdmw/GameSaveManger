using System.Diagnostics;
using GameSaveManager.Application.Launching;

namespace GameSaveManager.Infrastructure.Launching;

/// <summary>通过 Windows Shell 直接请求启动游戏入口，不经由 Explorer 或命令解释器中转。</summary>
public sealed class WindowsGameLaunchService(IGameProcessDetectionService? processDetectionService = null) : IGameLaunchService
{
    private readonly IGameProcessDetectionService _processDetectionService = processDetectionService ?? new WindowsGameProcessDetectionService();
    public Task<GameLaunchResult> LaunchAsync(
        GameLaunchProfile profile,
        string? installDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        ProcessStartInfo startInfo = CreateStartInfo(profile);
        ProcessSnapshot before = _processDetectionService.CaptureSnapshot();
        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
        using Process? process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows 未接受游戏启动请求。");

        return CreateResultAsync(process, before, profile, installDirectory, requestedAt, startInfo.WorkingDirectory, cancellationToken);
    }

    private async Task<GameLaunchResult> CreateResultAsync(Process process, ProcessSnapshot before, GameLaunchProfile profile, string? installDirectory, DateTimeOffset requestedAt, string workingDirectory, CancellationToken cancellationToken)
    {
        IReadOnlyList<DetectedGameProcess> detected = await _processDetectionService.DetectNewProcessesAsync(before, installDirectory, profile.MonitoredProcessNames, TimeSpan.FromSeconds(15), cancellationToken);
        return new GameLaunchResult(true, TryGetProcessId(process), requestedAt, profile.Target, workingDirectory, detected, detected.Count == 0 ? "未在 15 秒内检测到持续运行的游戏进程。" : null);
    }
    private static ProcessStartInfo CreateStartInfo(GameLaunchProfile profile) => profile.TargetType switch
    {
        GameLaunchTargetType.Executable => CreateExecutableStartInfo(profile),
        GameLaunchTargetType.Shortcut => CreateShortcutStartInfo(profile),
        GameLaunchTargetType.StoreUri => CreateStoreUriStartInfo(profile),
        _ => throw new ArgumentOutOfRangeException(nameof(profile.TargetType), profile.TargetType, "不支持的游戏启动入口类型。")
    };

    private static ProcessStartInfo CreateExecutableStartInfo(GameLaunchProfile profile)
    {
        string target = GetExistingLocalTarget(profile.Target, ".exe");
        ValidateArguments(profile.Arguments);
        string workingDirectory = GetWorkingDirectory(profile.WorkingDirectory, target);
        return new ProcessStartInfo
        {
            FileName = target,
            Arguments = profile.Arguments ?? string.Empty,
            WorkingDirectory = workingDirectory,
            UseShellExecute = true,
            Verb = profile.RunAsAdministrator ? "runas" : "open"
        };
    }

    private static ProcessStartInfo CreateShortcutStartInfo(GameLaunchProfile profile)
    {
        string target = GetExistingLocalTarget(profile.Target, ".lnk");
        return new ProcessStartInfo
        {
            FileName = target,
            UseShellExecute = true,
            Verb = "open"
        };
    }

    private static ProcessStartInfo CreateStoreUriStartInfo(GameLaunchProfile profile)
    {
        if (!Uri.TryCreate(profile.Target, UriKind.Absolute, out Uri? target) ||
            (target.Scheme is not "steam" and not "com.epicgames.launcher"))
        {
            throw new InvalidOperationException("只允许启动 Steam 或 Epic 游戏 URI。");
        }

        return new ProcessStartInfo(profile.Target) { UseShellExecute = true, Verb = "open" };
    }

    private static string GetExistingLocalTarget(string target, string extension)
    {
        if (string.IsNullOrWhiteSpace(target)) throw new InvalidOperationException("未配置游戏启动入口。");
        string fullPath = Path.GetFullPath(target);
        if (Uri.TryCreate(fullPath, UriKind.Absolute, out Uri? uri) && uri.IsUnc)
            throw new InvalidOperationException("本地游戏启动入口不能使用网络 UNC 路径。");
        if (!string.Equals(Path.GetExtension(fullPath), extension, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath))
            throw new InvalidOperationException($"未找到有效的游戏 {extension} 启动入口。");
        return fullPath;
    }

    private static string GetWorkingDirectory(string? configuredDirectory, string target)
    {
        string workingDirectory = string.IsNullOrWhiteSpace(configuredDirectory)
            ? Path.GetDirectoryName(target) ?? throw new InvalidOperationException("无法确定游戏工作目录。")
            : Path.GetFullPath(configuredDirectory);
        if (!Directory.Exists(workingDirectory)) throw new DirectoryNotFoundException("游戏工作目录不存在。");
        return workingDirectory;
    }

    private static void ValidateArguments(string? arguments)
    {
        if (arguments?.Length > 4096) throw new InvalidOperationException("游戏启动参数不能超过 4096 个字符。");
    }

    private static int? TryGetProcessId(Process process)
    {
        try { return process.Id; }
        catch (InvalidOperationException) { return null; }
    }
}
