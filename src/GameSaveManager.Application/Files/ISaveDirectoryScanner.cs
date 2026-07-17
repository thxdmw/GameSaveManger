using GameSaveManager.Application.Games;

namespace GameSaveManager.Application.Files;

public interface ISaveDirectoryScanner
{
    Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(string saveDirectory, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(SaveRootRule rule, CancellationToken cancellationToken);
    Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(
        SaveRootRule rule,
        int maximumFiles,
        CancellationToken cancellationToken);
    Task<SaveDirectoryScanResult> ScanWithBudgetAsync(
        SaveRootRule rule,
        SaveDirectoryScanBudget budget,
        CancellationToken cancellationToken);
}

public sealed record SaveDirectoryScanBudget(
    int MaximumMatchedFiles,
    int MaximumVisitedFiles,
    int MaximumVisitedDirectories,
    TimeSpan MaximumDuration)
{
    public static SaveDirectoryScanBudget CreateDefault(int maximumMatchedFiles) =>
        new(maximumMatchedFiles, 100_000, 10_000, TimeSpan.FromSeconds(30));
}

public sealed record SaveDirectoryScanResult(
    IReadOnlyList<ScannedSaveFile> Files,
    int VisitedFileCount,
    int VisitedDirectoryCount,
    bool WasTruncated,
    string? TruncationReason);
