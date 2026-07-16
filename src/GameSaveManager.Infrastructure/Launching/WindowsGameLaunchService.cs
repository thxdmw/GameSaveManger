using System.Diagnostics;
using GameSaveManager.Application.Launching;

namespace GameSaveManager.Infrastructure.Launching;

/// <summary>通过 Windows Shell 启动游戏，并只把与目标 EXE 或游戏目录相关的进程视为游戏进程。</summary>
public sealed class WindowsGameLaunchService(IGameProcessDetectionService? processDetectionService = null) : IGameLaunchService
{
    private readonly IGameProcessDetectionService _processDetectionService = processDetectionService ?? new WindowsGameProcessDetectionService();

    public async Task<GameLaunchResult> LaunchAsync(
        GameLaunchProfile profile,
        string? installDirectory,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(profile);

        ProcessStartInfo startInfo = WindowsGameLaunchStartInfoFactory.Create(profile);
        ProcessSnapshot before = _processDetectionService.CaptureSnapshot();
        DateTimeOffset requestedAt = DateTimeOffset.UtcNow;
        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Windows 未接受游戏启动请求。");

        string? detectionDirectory = string.IsNullOrWhiteSpace(installDirectory)
            ? string.IsNullOrWhiteSpace(startInfo.WorkingDirectory) ? null : startInfo.WorkingDirectory
            : installDirectory;
        IReadOnlyList<string> expectedNames = GameProcessNameRules.GetEffectiveNames(profile);
        IReadOnlyList<DetectedGameProcess> detected = await _processDetectionService.DetectNewProcessesAsync(
            before,
            detectionDirectory,
            expectedNames,
            TimeSpan.FromSeconds(15),
            cancellationToken);

        int? launchedProcessId = TryGetProcessId(process);
        DetectedGameProcess[] relevant = detected.Where(candidate =>
            candidate.IsInsideGameDirectory ||
            expectedNames.Contains(candidate.ProcessName, StringComparer.OrdinalIgnoreCase) ||
            profile.TargetType == GameLaunchTargetType.Executable && candidate.ProcessId == launchedProcessId)
            .ToArray();
        DetectedGameProcess[] running = relevant.Where(candidate => candidate.IsStillRunning).ToArray();
        string? warning = running.Length > 0
            ? null
            : relevant.Length > 0
                ? "与启动配置匹配的游戏进程已经退出，游戏可能显示了启动错误。"
                : "未检测到与目标 EXE 或游戏目录匹配的运行进程；不会把其他系统进程记录为游戏进程。";

        return new GameLaunchResult(
            true,
            launchedProcessId,
            requestedAt,
            profile.Target,
            startInfo.WorkingDirectory,
            relevant,
            warning);
    }

    private static int? TryGetProcessId(Process process)
    {
        try { return process.Id; }
        catch (InvalidOperationException) { return null; }
    }
}
