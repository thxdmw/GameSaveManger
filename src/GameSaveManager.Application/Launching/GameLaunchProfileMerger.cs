using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Application.Launching;

/// <summary>保存存档设置时只合并启动入口变化，避免覆盖用户参数、工作目录和已确认进程。</summary>
public sealed class GameLaunchProfileMerger : IGameLaunchProfileMerger
{
    public GameLaunchProfile? Merge(
        GameLaunchProfile? existing,
        GameIdentity currentIdentity,
        string? selectedLaunchTarget,
        string? legacyProcessName)
    {
        if (IsStoreIdentity(currentIdentity))
        {
            // 平台游戏的进程身份来自当前平台发现结果。界面中的兼容字段可能仍在切换中，
            // 不能把它无条件追加到另一款平台游戏的监控进程列表。
            if (existing?.TargetType == GameLaunchTargetType.StoreUri)
                return Sanitize(existing, currentIdentity.ProcessName);
            return CreateDefault(currentIdentity);
        }

        string? target = string.IsNullOrWhiteSpace(selectedLaunchTarget)
            ? currentIdentity.ExecutablePath
            : Path.GetFullPath(selectedLaunchTarget);
        if (string.IsNullOrWhiteSpace(target)) return existing is null ? null : Sanitize(existing, legacyProcessName);

        GameLaunchTargetType targetType = string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase)
            ? GameLaunchTargetType.Shortcut
            : GameLaunchTargetType.Executable;
        if (existing is null)
        {
            return new GameLaunchProfile(
                targetType,
                target,
                null,
                targetType == GameLaunchTargetType.Executable ? Path.GetDirectoryName(target) : null,
                false,
                SafeNames(currentIdentity.ProcessName, legacyProcessName, target));
        }

        bool targetChanged = !string.Equals(existing.Target, target, StringComparison.OrdinalIgnoreCase);
        string? workingDirectory = existing.WorkingDirectory;
        if (targetChanged)
        {
            string? oldDefault = existing.TargetType == GameLaunchTargetType.Executable
                ? Path.GetDirectoryName(existing.Target)
                : null;
            if (string.IsNullOrWhiteSpace(workingDirectory)
                || string.Equals(workingDirectory, oldDefault, StringComparison.OrdinalIgnoreCase))
            {
                workingDirectory = targetType == GameLaunchTargetType.Executable
                    ? Path.GetDirectoryName(target)
                    : workingDirectory;
            }
        }

        return new GameLaunchProfile(
            targetType,
            target,
            existing.Arguments,
            workingDirectory,
            existing.RunAsAdministrator,
            SafeNames(existing.MonitoredProcessNames.Concat([currentIdentity.ProcessName, legacyProcessName, target]).ToArray()));
    }

    private static GameLaunchProfile? CreateDefault(GameIdentity identity)
    {
        IReadOnlyList<string> names = SafeNames(identity.ProcessName);
        if (string.Equals(identity.Provider, GameIdentity.Steam, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(identity.ProviderGameId))
            return new GameLaunchProfile(GameLaunchTargetType.StoreUri, $"steam://run/{Uri.EscapeDataString(identity.ProviderGameId)}", null, null, false, names);
        if (string.Equals(identity.Provider, GameIdentity.Epic, StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(identity.ProviderGameId))
            return new GameLaunchProfile(GameLaunchTargetType.StoreUri, $"com.epicgames.launcher://apps/{Uri.EscapeDataString(identity.ProviderGameId)}?action=launch&silent=true", null, null, false, names);
        if (string.IsNullOrWhiteSpace(identity.ExecutablePath)) return null;
        string target = Path.GetFullPath(identity.ExecutablePath);
        GameLaunchTargetType type = string.Equals(Path.GetExtension(target), ".lnk", StringComparison.OrdinalIgnoreCase)
            ? GameLaunchTargetType.Shortcut
            : GameLaunchTargetType.Executable;
        return new GameLaunchProfile(type, target, null, type == GameLaunchTargetType.Executable ? Path.GetDirectoryName(target) : null, false, SafeNames(identity.ProcessName, target));
    }

    private static bool IsStoreIdentity(GameIdentity identity) =>
        string.Equals(identity.Provider, GameIdentity.Steam, StringComparison.OrdinalIgnoreCase)
        || string.Equals(identity.Provider, GameIdentity.Epic, StringComparison.OrdinalIgnoreCase);

    private static GameLaunchProfile Sanitize(GameLaunchProfile profile, string? legacyProcessName) =>
        profile with { MonitoredProcessNames = SafeNames(profile.MonitoredProcessNames.Concat([legacyProcessName]).ToArray()) };

    private static IReadOnlyList<string> SafeNames(params string?[] values) =>
        values.Select(GameProcessNameRules.Normalize)
            .Where(value => value.Length > 0 && !GameProcessNameRules.IsUnsafeGenericName(value))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
}
