namespace GameSaveManager.Application.Launching;

public sealed record GameLaunchResult(
    bool LaunchRequestSucceeded,
    int? InitialProcessId,
    DateTimeOffset RequestedAt,
    string Target,
    string WorkingDirectory,
    IReadOnlyList<DetectedGameProcess> DetectedProcesses,
    string? Warning);

public sealed record DetectedGameProcess(
    int ProcessId,
    string ProcessName,
    string? ExecutablePath,
    bool IsInsideGameDirectory,
    bool IsStillRunning);
