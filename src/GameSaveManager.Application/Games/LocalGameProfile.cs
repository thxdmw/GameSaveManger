namespace GameSaveManager.Application.Games;

using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Launching;

public sealed record LocalGameProfile(
    string ServerKey,
    string GameId,
    string Provider,
    string? ProviderGameId,
    string? InstallDirectory,
    string SaveDirectory,
    string ProcessName,
    string? ExecutablePath,
    SaveLocationSource SaveDirectorySource,
    int SaveDirectoryConfidence,
    bool UserConfirmed,
    bool AutoSnapshotEnabled,
    IReadOnlyList<SaveRootRule>? SaveRoots = null,
    IReadOnlyList<RegistrySaveRule>? RegistrySaveRules = null,
    string? IdentityExecutablePath = null,
    GameLaunchProfile? LaunchProfile = null)
{
    public GameLaunchProfile? EffectiveLaunchProfile => LaunchProfile ?? (string.IsNullOrWhiteSpace(ExecutablePath) ? null : new GameLaunchProfile(
        Path.GetExtension(ExecutablePath).Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? GameLaunchTargetType.Shortcut : GameLaunchTargetType.Executable, ExecutablePath, null, Path.GetDirectoryName(ExecutablePath), false,
        string.IsNullOrWhiteSpace(ProcessName) ? [] : [Path.GetFileNameWithoutExtension(ProcessName)]));

    public IReadOnlyList<SaveRootRule> EffectiveSaveRoots => SaveRoots is { Count: > 0 }
        ? SaveRoots
        : [SaveRootRule.CreateDefault(SaveDirectory, SaveDirectorySource, SaveDirectoryConfidence, UserConfirmed)];

    public IReadOnlyList<RegistrySaveRule> EffectiveRegistrySaveRules => RegistrySaveRules ?? [];
}