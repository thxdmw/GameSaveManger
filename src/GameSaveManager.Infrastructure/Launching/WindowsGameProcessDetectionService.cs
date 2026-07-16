using System.ComponentModel;
using System.Diagnostics;
using GameSaveManager.Application.Launching;

namespace GameSaveManager.Infrastructure.Launching;

public sealed class WindowsGameProcessDetectionService : IGameProcessDetectionService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    private static readonly TimeSpan StableDuration = TimeSpan.FromSeconds(5);

    public ProcessSnapshot CaptureSnapshot() => new(SnapshotProcesses());

    public async Task<IReadOnlyList<DetectedGameProcess>> DetectNewProcessesAsync(
        ProcessSnapshot before,
        string? installDirectory,
        IReadOnlyList<string> expectedProcessNames,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        HashSet<int> existingIds = before.Processes.Select(process => process.ProcessId).ToHashSet();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        DateTimeOffset deadline = startedAt.Add(timeout);
        var observations = new Dictionary<int, ProcessObservation>();

        while (DateTimeOffset.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            IReadOnlyList<DetectedGameProcess> snapshot = SnapshotProcesses();
            HashSet<int> runningIds = snapshot.Select(process => process.ProcessId).ToHashSet();

            foreach (DetectedGameProcess process in snapshot)
            {
                if (existingIds.Contains(process.ProcessId)) continue;
                DetectedGameProcess candidate = process with
                {
                    IsInsideGameDirectory = IsInsideDirectory(process.ExecutablePath, installDirectory),
                    IsStillRunning = true
                };
                if (observations.TryGetValue(process.ProcessId, out ProcessObservation? observation))
                    observations[process.ProcessId] = observation with { Process = candidate, LastSeenAt = observedAt };
                else
                    observations[process.ProcessId] = new ProcessObservation(candidate, observedAt, observedAt);
            }

            foreach ((int processId, ProcessObservation observation) in observations.ToArray())
            {
                if (!runningIds.Contains(processId))
                    observations[processId] = observation with { Process = observation.Process with { IsStillRunning = false } };
            }

            ProcessObservation[] stableRunning = observations.Values
                .Where(observation => observation.Process.IsStillRunning && observedAt - observation.FirstSeenAt >= StableDuration)
                .ToArray();
            bool stableCandidate = stableRunning.Any(observation =>
                observation.Process.IsInsideGameDirectory || MatchesExpectedName(observation.Process.ProcessName, expectedProcessNames));
            if (stableCandidate) break;

            TimeSpan remaining = deadline - DateTimeOffset.UtcNow;
            if (remaining > TimeSpan.Zero)
                await Task.Delay(remaining < PollInterval ? remaining : PollInterval, cancellationToken);
        }

        return observations.Values
            .Select(observation => observation.Process)
            .OrderByDescending(process => Score(process, expectedProcessNames))
            .ThenBy(process => process.ProcessName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IReadOnlyList<DetectedGameProcess> SnapshotProcesses()
    {
        var result = new List<DetectedGameProcess>();
        foreach (Process process in Process.GetProcesses())
        {
            using (process)
            {
                try
                {
                    if (!process.HasExited)
                        result.Add(new DetectedGameProcess(process.Id, process.ProcessName, TryGetExecutablePath(process), false, true));
                }
                catch (InvalidOperationException) { }
                catch (Win32Exception) { }
            }
        }
        return result;
    }

    private static string? TryGetExecutablePath(Process process)
    {
        try { return process.MainModule?.FileName; }
        catch (Win32Exception) { return null; }
        catch (InvalidOperationException) { return null; }
    }

    private static bool IsInsideDirectory(string? path, string? directory)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(directory)) return false;
        try
        {
            string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
            return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool MatchesExpectedName(string processName, IReadOnlyList<string> expectedProcessNames) =>
        expectedProcessNames.Any(name => string.Equals(Path.GetFileNameWithoutExtension(name), processName, StringComparison.OrdinalIgnoreCase));

    private static bool IsHelperProcess(string processName) =>
        processName.Contains("launcher", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("setup", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("crash", StringComparison.OrdinalIgnoreCase) ||
        processName.Contains("reporter", StringComparison.OrdinalIgnoreCase);

    private static int Score(DetectedGameProcess process, IReadOnlyList<string> expectedProcessNames)
    {
        int score = 50;
        if (process.IsInsideGameDirectory) score += 40;
        if (MatchesExpectedName(process.ProcessName, expectedProcessNames)) score += 40;
        if (process.IsStillRunning) score += 20;
        else score -= 30;
        if (IsHelperProcess(process.ProcessName)) score -= 50;

        return score;
    }

    private sealed record ProcessObservation(DetectedGameProcess Process, DateTimeOffset FirstSeenAt, DateTimeOffset LastSeenAt);
}