namespace GameSaveManager.Application.Launching;

public interface IGameLaunchService
{
    Task<GameLaunchResult> LaunchAsync(
        GameLaunchProfile profile,
        string? installDirectory,
        CancellationToken cancellationToken);
}
