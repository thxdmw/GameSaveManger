using System.Diagnostics;
using GameSaveManager.Infrastructure.Launching;

namespace GameSaveManager.Verification;

internal static class GameProcessDetectionVerification
{
    public static async Task VerifyDelayedPollingAndStableProcessAsync()
    {
        var detector = new WindowsGameProcessDetectionService();
        var before = detector.CaptureSnapshot();
        using Process process = Process.Start(new ProcessStartInfo
        {
            FileName = "powershell.exe",
            Arguments = "-NoProfile -NonInteractive -Command Start-Sleep -Seconds 8",
            UseShellExecute = false,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("无法启动进程检测验证程序。");

        try
        {
            IReadOnlyList<GameSaveManager.Application.Launching.DetectedGameProcess> detected =
                await detector.DetectNewProcessesAsync(
                    before,
                    null,
                    ["powershell.exe"],
                    TimeSpan.FromSeconds(7),
                    CancellationToken.None);
            GameSaveManager.Application.Launching.DetectedGameProcess? candidate =
                detected.FirstOrDefault(item => item.ProcessId == process.Id);
            if (candidate is null) throw new InvalidOperationException("检测器应持续轮询，而不是在首次无候选时立即返回。");
            Ensure(candidate.IsStillRunning, "稳定运行五秒的目标进程应标记为正在运行。");
        }
        finally
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException) { }
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
