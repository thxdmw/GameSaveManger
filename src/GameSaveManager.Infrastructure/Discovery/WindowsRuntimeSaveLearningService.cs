using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>仅采集受限范围内的路径、大小与修改时间，不读取文件内容。</summary>
public sealed class WindowsRuntimeSaveLearningService(RuntimeLearningOptions? options = null) : IRuntimeSaveLearningService
{
    private readonly RuntimeLearningOptions _options = options ?? new RuntimeLearningOptions();

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

    private IReadOnlyList<FileMetadataSnapshot> Capture(IEnumerable<string> roots, CancellationToken cancellationToken)
    {
        var files = new List<FileMetadataSnapshot>();
        var excluded = new HashSet<string>(_options.EffectiveExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
        foreach (string root in roots.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            int rootCount = 0;
            var pending = new Queue<(string Path, int Depth)>();
            pending.Enqueue((root, 0));
            while (pending.Count > 0 && rootCount < _options.MaxFilesPerRoot && files.Count < _options.MaxTotalFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                (string directory, int depth) = pending.Dequeue();
                try
                {
                    var enumeration = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
                    foreach (string path in Directory.EnumerateFiles(directory, "*", enumeration))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (rootCount >= _options.MaxFilesPerRoot || files.Count >= _options.MaxTotalFiles) break;
                        var info = new FileInfo(path);
                        files.Add(new FileMetadataSnapshot(path, info.Length, info.LastWriteTimeUtc));
                        rootCount++;
                    }
                    if (depth >= _options.MaxDepth) continue;
                    foreach (string child in Directory.EnumerateDirectories(directory, "*", enumeration))
                    {
                        if (!excluded.Contains(Path.GetFileName(child))) pending.Enqueue((child, depth + 1));
                    }
                }
                catch (UnauthorizedAccessException) { }
                catch (IOException) { }
            }
            if (files.Count >= _options.MaxTotalFiles) break;
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
