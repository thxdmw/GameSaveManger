namespace GameSaveManager.Application.Discovery;

public interface IExecutableGameIdentityFactory
{
    Task<GameIdentity> CreateAsync(string executablePath, CancellationToken cancellationToken);
}
