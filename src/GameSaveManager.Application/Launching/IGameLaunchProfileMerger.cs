using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Application.Launching;

public interface IGameLaunchProfileMerger
{
    GameLaunchProfile? Merge(
        GameLaunchProfile? existing,
        GameIdentity currentIdentity,
        string? selectedLaunchTarget,
        string? legacyProcessName);
}
