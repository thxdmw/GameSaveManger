namespace GameSaveManager.Application.Launching;

/// <summary>集中处理进程名，避免把常驻的 Windows 系统进程保存成游戏进程。</summary>
public static class GameProcessNameRules
{
    private static readonly HashSet<string> UnsafeGenericNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "registry", "smss", "csrss", "wininit", "winlogon",
        "services", "lsass", "svchost", "explorer", "sihost", "taskhostw",
        "rundll32", "dllhost", "conhost", "cmd", "powershell", "pwsh",
        "msiexec", "werfault", "wermgr"
    };

    public static bool IsUnsafeGenericName(string? processName)
    {
        string normalized = Normalize(processName);
        return normalized.Length == 0 || UnsafeGenericNames.Contains(normalized);
    }

    public static IReadOnlyList<string> GetEffectiveNames(
        GameLaunchProfile? profile,
        string? legacyProcessName = null)
    {
        var names = new List<string>();
        if (profile is { TargetType: GameLaunchTargetType.Executable })
            Add(profile.Target);
        if (profile?.MonitoredProcessNames is { } configured)
        {
            foreach (string name in configured) Add(name);
        }
        Add(legacyProcessName);
        return names.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

        void Add(string? value)
        {
            string normalized = Normalize(value);
            if (normalized.Length > 0 && !UnsafeGenericNames.Contains(normalized)) names.Add(normalized);
        }
    }

    public static string Normalize(string? processName) => string.IsNullOrWhiteSpace(processName)
        ? string.Empty
        : Path.GetFileNameWithoutExtension(processName.Trim());
}
