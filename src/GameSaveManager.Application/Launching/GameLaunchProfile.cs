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
    // 用户额外输入的参数；启动快捷方式时不会混入快捷方式内部参数。
    string? Arguments,
    string? WorkingDirectory,
    bool RunAsAdministrator,
    IReadOnlyList<string> MonitoredProcessNames,
    // 快捷方式内部参数，仅用于展示和诊断，不会再次传给 .lnk。
    string? ShortcutArguments = null);
