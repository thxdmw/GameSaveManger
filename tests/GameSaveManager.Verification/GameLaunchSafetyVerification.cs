using GameSaveManager.Application.Launching;
using GameSaveManager.Infrastructure.Launching;

namespace GameSaveManager.Verification;

internal static class GameLaunchSafetyVerification
{
    public static async Task VerifySystemProcessIsNeverPersistedOrConfirmedAsync()
    {
        string target = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "where.exe");
        var profile = new GameLaunchProfile(
            GameLaunchTargetType.Executable,
            target,
            "/Q cmd.exe",
            Path.GetDirectoryName(target),
            false,
            ["svchost.exe"]);

        IReadOnlyList<string> effectiveNames = GameProcessNameRules.GetEffectiveNames(profile, "svchost.exe");
        Ensure(effectiveNames.SequenceEqual(["where"], StringComparer.OrdinalIgnoreCase),
            "直接 EXE 应使用目标文件名，并丢弃 svchost 等系统进程。");

        var detector = new UnrelatedProcessDetector();
        var service = new WindowsGameLaunchService(detector);
        GameLaunchResult result = await service.LaunchAsync(profile, Path.GetDirectoryName(target), CancellationToken.None);

        Ensure(detector.ExpectedNames.SequenceEqual(["where"], StringComparer.OrdinalIgnoreCase),
            "启动检测不应继续使用错误保存的 svchost 进程名。");
        Ensure(result.DetectedProcesses.Count == 0,
            "与目标名称和游戏目录均无关的进程不能作为游戏候选。");
        Ensure(result.Warning is not null, "只有无关进程时不能报告游戏启动成功。");

        string shortcutDirectory = Path.Combine(Path.GetTempPath(), "GameSaveManager.Shortcut", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(shortcutDirectory);
        string shortcut = Path.Combine(shortcutDirectory, "game.lnk");
        await File.WriteAllBytesAsync(shortcut, [0]);
        try
        {
            var shortcutProfile = new GameLaunchProfile(
                GameLaunchTargetType.Shortcut, shortcut, "--user-extra", shortcutDirectory,
                false, ["game"], "--inside-shortcut");
            System.Diagnostics.ProcessStartInfo startInfo =
                WindowsGameLaunchStartInfoFactory.Create(shortcutProfile);
            Ensure(startInfo.Arguments == "--user-extra",
                "启动 .lnk 时只能传递用户额外参数，不能重复快捷方式内部参数。");
        }
        finally
        {
            try { Directory.Delete(shortcutDirectory, recursive: true); } catch (IOException) { }
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class UnrelatedProcessDetector : IGameProcessDetectionService
    {
        public IReadOnlyList<string> ExpectedNames { get; private set; } = [];

        public ProcessSnapshot CaptureSnapshot() => new([]);

        public Task<IReadOnlyList<DetectedGameProcess>> DetectNewProcessesAsync(
            ProcessSnapshot before,
            string? installDirectory,
            IReadOnlyList<string> expectedProcessNames,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            ExpectedNames = expectedProcessNames;
            IReadOnlyList<DetectedGameProcess> result =
                [new DetectedGameProcess(42, "svchost", null, false, true)];
            return Task.FromResult(result);
        }
    }
}
