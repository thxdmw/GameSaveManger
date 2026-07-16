namespace GameSaveManager.Application.Launching;

public enum GameLaunchTargetType
{
    Executable,
    Shortcut,
    StoreUri
}

public sealed record GameLaunchProfile(
    GameLaunchTargetType TargetType,
    string Target,
    string? Arguments,
    string? WorkingDirectory,
    bool RunAsAdministrator,
    IReadOnlyList<string> MonitoredProcessNames);
