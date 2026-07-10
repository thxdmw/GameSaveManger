namespace GameSaveManager.Application.Files;

public interface ISaveDirectoryScanner
{
    Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(
        string saveDirectory,
        CancellationToken cancellationToken);
}
