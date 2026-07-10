namespace GameSaveManager.Application.Files;

public interface IFileHashService
{
    Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken);
}
