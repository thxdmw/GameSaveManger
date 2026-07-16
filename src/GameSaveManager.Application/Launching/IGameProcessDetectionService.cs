namespace GameSaveManager.Application.Launching;

public sealed record ProcessSnapshot(IReadOnlyList<DetectedGameProcess> Processes);

public interface IGameProcessDetectionService
{
    ProcessSnapshot CaptureSnapshot();

    Task<IReadOnlyList<DetectedGameProcess>> DetectNewProcessesAsync(
        ProcessSnapshot before,
        string? installDirectory,
        IReadOnlyList<string> expectedProcessNames,
        TimeSpan timeout,
        CancellationToken cancellationToken);
}
