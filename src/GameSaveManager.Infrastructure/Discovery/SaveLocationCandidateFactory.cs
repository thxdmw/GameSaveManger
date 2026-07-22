using System.Diagnostics;
using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

internal static class SaveLocationCandidateFactory
{
    public static SaveLocationCandidate? Create(
        string directory,
        int confidence,
        SaveLocationSource source,
        string reason,
        bool confirmation = true,
        CancellationToken cancellationToken = default,
        TimeSpan? scanBudget = null)
    {
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            if (!Directory.Exists(fullPath)) return null;
            var files = new List<FileInfo>();
            var pending = new Queue<(string Directory, int Depth)>();
            pending.Enqueue((fullPath, 0));
            int visitedDirectories = 0;
            bool truncated = false;
            var stopwatch = Stopwatch.StartNew();
            TimeSpan effectiveBudget = scanBudget is { } requested && requested > TimeSpan.Zero
                ? TimeSpan.FromTicks(Math.Min(requested.Ticks, TimeSpan.FromSeconds(5).Ticks))
                : TimeSpan.FromSeconds(5);
            while (pending.Count > 0 && !truncated)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string current, int depth) = pending.Dequeue();
                if (++visitedDirectories > 2_000 || stopwatch.Elapsed > effectiveBudget)
                {
                    truncated = true;
                    break;
                }
                try
                {
                    foreach (string path in Directory.EnumerateFiles(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0) continue;
                        files.Add(new FileInfo(path));
                        if (files.Count >= 10_000)
                        {
                            truncated = true;
                            break;
                        }
                    }
                    if (truncated || depth >= 8) continue;
                    foreach (string child in Directory.EnumerateDirectories(current, "*", SearchOption.TopDirectoryOnly))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                            pending.Enqueue((child, depth + 1));
                    }
                }
                catch (Exception exception) when (exception is UnauthorizedAccessException or IOException) { }
            }
            IReadOnlyList<string> samples = files.OrderByDescending(file => file.LastWriteTimeUtc).Take(5)
                .Select(file => Path.GetRelativePath(fullPath, file.FullName)).ToArray();
            string effectiveReason = truncated ? reason + "；目录较大，文件统计已按安全预算截断" : reason;
            return new SaveLocationCandidate(fullPath, Math.Clamp(confidence, 0, 100), source, effectiveReason,
                files.Count, files.Sum(file => file.Length), files.Count == 0 ? null : files.Max(file => file.LastWriteTimeUtc), samples, confirmation);
        }
        catch (OperationCanceledException) { throw; }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }
}
