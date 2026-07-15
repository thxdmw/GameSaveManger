namespace GameSaveManager.Application.Discovery;

public interface IRuntimeSaveLearningService
{
    Task<IReadOnlyList<FileMetadataSnapshot>> CaptureBeforeAsync(GameIdentity game, CancellationToken cancellationToken);

    Task<IReadOnlyList<SaveLocationCandidate>> DetectChangesAsync(
        GameIdentity game,
        IReadOnlyList<FileMetadataSnapshot> before,
        IProgress<SaveDetectionProgress>? progress,
        CancellationToken cancellationToken);
}
