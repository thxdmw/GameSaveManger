using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

internal static class SaveLocationCandidateFactory
{
    public static SaveLocationCandidate? Create(string directory, int confidence, SaveLocationSource source, string reason, bool confirmation = true)
    {
        try
        {
            string fullPath = Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (!Directory.Exists(fullPath)) return null;
            var files = new List<FileInfo>();
            foreach (string path in Directory.EnumerateFiles(fullPath, "*", SearchOption.AllDirectories))
            {
                try
                {
                    files.Add(new FileInfo(path));
                    if (files.Count >= 10_000) break;
                }
                catch (UnauthorizedAccessException) { }
            }
            IReadOnlyList<string> samples = files.OrderByDescending(file => file.LastWriteTimeUtc).Take(5)
                .Select(file => Path.GetRelativePath(fullPath, file.FullName)).ToArray();
            return new SaveLocationCandidate(fullPath, Math.Clamp(confidence, 0, 100), source, reason,
                files.Count, files.Sum(file => file.Length), files.Count == 0 ? null : files.Max(file => file.LastWriteTimeUtc), samples, confirmation);
        }
        catch (UnauthorizedAccessException) { return null; }
        catch (IOException) { return null; }
    }
}
