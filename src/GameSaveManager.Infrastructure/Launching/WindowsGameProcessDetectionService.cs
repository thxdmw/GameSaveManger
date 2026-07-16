using System.ComponentModel;
using System.Diagnostics;
using GameSaveManager.Application.Launching;

namespace GameSaveManager.Infrastructure.Launching;

public sealed class WindowsGameProcessDetectionService : IGameProcessDetectionService
{
    public ProcessSnapshot CaptureSnapshot() => new(SnapshotProcesses());

    public async Task<IReadOnlyList<DetectedGameProcess>> DetectNewProcessesAsync(
        ProcessSnapshot before,
        string? installDirectory,
        IReadOnlyList<string> expectedProcessNames,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        HashSet<int> existingIds = before.Processes.Select(process => process.ProcessId).ToHashSet();
        DateTimeOffset deadline = DateTimeOffset.UtcNow.Add(timeout);
        var candidates = new Dictionary<int, DetectedGameProcess>();

        do
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (DetectedGameProcess process in SnapshotProcesses())
            {
                if (existingIds.Contains(process.ProcessId)) continue;
                candidates[process.ProcessId] = process with
                {
                    IsInsideGameDirectory = IsInsideDirectory(process.ExecutablePath, installDirectory)
                };
            }

            if (candidates.Count > 0 && DateTimeOffset.UtcNow.AddSeconds(5) <= deadline)
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
            else
                break;
        } while (DateTimeOffset.UtcNow < deadline);

        return candidates.Values
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
                    result.Add(new DetectedGameProcess(process.Id, process.ProcessName, TryGetExecutablePath(process), false, !process.HasExited));
                }
                catch (InvalidOperationException) { }
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
        string root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        return Path.GetFullPath(path).StartsWith(root, StringComparison.OrdinalIgnoreCase);
    }

    private static int Score(DetectedGameProcess process, IReadOnlyList<string> expectedProcessNames)
    {
        int score = 50;
        if (process.IsInsideGameDirectory) score += 40;
        if (expectedProcessNames.Any(name => string.Equals(Path.GetFileNameWithoutExtension(name), process.ProcessName, StringComparison.OrdinalIgnoreCase))) score += 40;
        if (process.ProcessName.Contains("launcher", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Contains("crash", StringComparison.OrdinalIgnoreCase) || process.ProcessName.Contains("reporter", StringComparison.OrdinalIgnoreCase)) score -= 50;
        return score;
    }
}
