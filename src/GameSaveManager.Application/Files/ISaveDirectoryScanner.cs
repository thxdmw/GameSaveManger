using GameSaveManager.Application.Games;

namespace GameSaveManager.Application.Files;

public interface ISaveDirectoryScanner
{
    Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(string saveDirectory, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(SaveRootRule rule, CancellationToken cancellationToken);
}
