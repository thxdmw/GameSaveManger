using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

public sealed class WindowsRuntimeSaveLearningService : IRuntimeSaveLearningService
{
    public Task<IReadOnlyList<FileMetadataSnapshot>> CaptureBeforeAsync(GameIdentity game, CancellationToken cancellationToken) =>
        Task.Run(() => Capture(GetRoots(game), cancellationToken), cancellationToken);

    public Task<IReadOnlyList<SaveLocationCandidate>> DetectChangesAsync(GameIdentity game, IReadOnlyList<FileMetadataSnapshot> before,
        IProgress<SaveDetectionProgress>? progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        progress?.Report(new SaveDetectionProgress("学习", "正在比较游戏运行前后的文件变化…", 0, 2));
        Dictionary<string, FileMetadataSnapshot> oldFiles = before.ToDictionary(file => file.FullPath, StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<FileMetadataSnapshot> after = Capture(GetRoots(game), cancellationToken);
        var changed = after.Where(file => !oldFiles.TryGetValue(file.FullPath, out FileMetadataSnapshot? old) || old.Size != file.Size || old.LastWriteTimeUtc != file.LastWriteTimeUtc).ToArray();
        var candidates = changed.GroupBy(file => Path.GetDirectoryName(file.FullPath)!, StringComparer.OrdinalIgnoreCase)
            .Select(group => SaveLocationCandidateFactory.Create(group.Key, Score(group.Key), SaveLocationSource.RuntimeLearning,
                $"运行学习：检测到 {group.Count()} 个新增或修改文件"))
            .Where(candidate => candidate is not null).Cast<SaveLocationCandidate>()
            .OrderByDescending(candidate => candidate.Confidence).ToArray();
        progress?.Report(new SaveDetectionProgress("学习", $"完成：检测到 {changed.Length} 个变化文件，等待用户确认。", 2, 2));
        return (IReadOnlyList<SaveLocationCandidate>)candidates;
    }, cancellationToken);

    private static IReadOnlyList<FileMetadataSnapshot> Capture(IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        var files = new List<FileMetadataSnapshot>();
        foreach (string root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                foreach (string path in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    try { var info = new FileInfo(path); files.Add(new FileMetadataSnapshot(path, info.Length, info.LastWriteTimeUtc)); }
                    catch (UnauthorizedAccessException) { }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
        return files;
    }

    private static IEnumerable<string> GetRoots(GameIdentity game)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        yield return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        yield return local;
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        yield return Path.Combine(local, "..", "LocalLow");
        yield return game.InstallDirectory;
    }

    private static int Score(string directory)
    {
        string value = directory.ToLowerInvariant();
        int score = 50;
        if (new[] { "save", "saved", "profile", "slot" }.Any(value.Contains)) score += 20;
        if (new[] { "cache", "temp", "log", "crash", "screenshot", "shader" }.Any(value.Contains)) score -= 60;
        return Math.Clamp(score, 0, 100);
    }
}
