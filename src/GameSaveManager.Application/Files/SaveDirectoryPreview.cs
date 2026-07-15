using GameSaveManager.Application.Games;

namespace GameSaveManager.Application.Files;

public sealed record SaveDirectoryPreview(int FileCount, long TotalSize, DateTime? LatestWriteTimeUtc,
    IReadOnlyList<string> RecentFiles, IReadOnlyList<string> LargestFiles, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> AppliedIncludes, IReadOnlyList<string> AppliedExcludes);

public interface ISaveDirectoryPreviewService
{
    Task<SaveDirectoryPreview> PreviewAsync(SaveRootRule rule, CancellationToken cancellationToken);
}
