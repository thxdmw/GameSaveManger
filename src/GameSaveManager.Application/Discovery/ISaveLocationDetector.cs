namespace GameSaveManager.Application.Discovery;

public interface ISaveLocationDetector
{
    Task<IReadOnlyList<SaveLocationCandidate>> DetectAsync(
        GameIdentity game,
        IProgress<SaveDetectionProgress>? progress,
        CancellationToken cancellationToken);
}
