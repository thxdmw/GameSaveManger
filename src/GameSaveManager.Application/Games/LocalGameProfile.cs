namespace GameSaveManager.Application.Games;

using GameSaveManager.Application.Discovery;

/// <summary>本地游戏配置；自动同步只能使用已确认的存档根目录。</summary>
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
    IReadOnlyList<RegistrySaveRule>? RegistrySaveRules = null)
{
    public IReadOnlyList<SaveRootRule> EffectiveSaveRoots => SaveRoots is { Count: > 0 }
        ? SaveRoots
        : [SaveRootRule.CreateDefault(SaveDirectory, SaveDirectorySource, SaveDirectoryConfidence, UserConfirmed)];
    public IReadOnlyList<RegistrySaveRule> EffectiveRegistrySaveRules => RegistrySaveRules ?? [];
}
