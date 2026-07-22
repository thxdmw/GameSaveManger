using GameSaveManager.Application.Discovery;

using System.Diagnostics;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>仅采集受限范围内的路径、大小与修改时间，不读取文件内容。</summary>
public sealed class WindowsRuntimeSaveLearningService(RuntimeLearningOptions? options = null) : IRuntimeSaveLearningService
{
    private readonly RuntimeLearningOptions _options = options ?? new RuntimeLearningOptions();

    public Task<IReadOnlyList<FileMetadataSnapshot>> CaptureBeforeAsync(GameIdentity game, CancellationToken cancellationToken) =>
        Task.Run(() => Capture(GetRoots(game), Stopwatch.StartNew(), cancellationToken), cancellationToken);

    public Task<IReadOnlyList<SaveLocationCandidate>> DetectChangesAsync(GameIdentity game, IReadOnlyList<FileMetadataSnapshot> before,
        IProgress<SaveDetectionProgress>? progress, CancellationToken cancellationToken) => Task.Run(() =>
    {
        ValidateOptions();
        var stopwatch = Stopwatch.StartNew();
        progress?.Report(new SaveDetectionProgress("学习", "正在比较游戏运行前后的文件变化…", 0, 2));
        Dictionary<string, FileMetadataSnapshot> oldFiles = before
            .GroupBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        IReadOnlyList<FileMetadataSnapshot> after = Capture(GetRoots(game), stopwatch, cancellationToken);
        Dictionary<string, FileMetadataSnapshot> newFiles = after
            .GroupBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.OrdinalIgnoreCase);
        FileMetadataSnapshot[] changed = after
            .Where(file => !oldFiles.TryGetValue(file.FullPath, out FileMetadataSnapshot? old)
                           || old.Size != file.Size
                           || old.LastWriteTimeUtc != file.LastWriteTimeUtc)
            .Concat(before.Where(file => !newFiles.ContainsKey(file.FullPath)))
            .GroupBy(file => file.FullPath, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Last())
            .ToArray();
        var candidates = new List<SaveLocationCandidate>();
        foreach ((string directory, int changeCount) in BuildCandidateDirectories(changed)
                     .OrderByDescending(item => Score(item.Directory, game.InstallDirectory))
                     .ThenByDescending(item => item.ChangeCount)
                     .ThenBy(item => item.Directory, StringComparer.OrdinalIgnoreCase)
                     .Take(_options.MaxCandidateDirectories))
        {
            cancellationToken.ThrowIfCancellationRequested();
            EnsureWithinBudget(stopwatch);
            TimeSpan remaining = TimeSpan.FromSeconds(_options.MaxScanSeconds) - stopwatch.Elapsed;
            SaveLocationCandidate? candidate = SaveLocationCandidateFactory.Create(
                directory,
                Score(directory, game.InstallDirectory),
                SaveLocationSource.RuntimeLearning,
                $"运行学习：检测到 {changeCount} 个新增、修改或删除文件，已合并相邻存档槽位",
                cancellationToken: cancellationToken,
                scanBudget: remaining);
            if (candidate is not null) candidates.Add(candidate);
        }
        EnsureWithinBudget(stopwatch);
        SaveLocationCandidate[] orderedCandidates = candidates
            .OrderByDescending(candidate => candidate.Confidence)
            .ThenByDescending(candidate => candidate.LatestWriteTimeUtc)
            .ToArray();
        progress?.Report(new SaveDetectionProgress("学习", $"完成：检测到 {changed.Length} 个变化文件，等待用户确认。", 2, 2));
        return (IReadOnlyList<SaveLocationCandidate>)orderedCandidates;
    }, cancellationToken);

    private IReadOnlyList<FileMetadataSnapshot> Capture(
        IEnumerable<string> roots,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        ValidateOptions();
        var files = new List<FileMetadataSnapshot>();
        var excluded = new HashSet<string>(_options.EffectiveExcludedDirectoryNames, StringComparer.OrdinalIgnoreCase);
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string root in NormalizeRoots(roots))
        {
            int rootCount = 0;
            var pending = new Queue<(string Path, int Depth)>();
            pending.Enqueue((root, 0));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                EnsureWithinBudget(stopwatch);
                (string directory, int depth) = pending.Dequeue();
                try
                {
                    var enumeration = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint, ReturnSpecialDirectories = false };
                    foreach (string path in Directory.EnumerateFiles(directory, "*", enumeration))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        EnsureWithinBudget(stopwatch);
                        if (rootCount >= _options.MaxFilesPerRoot)
                            throw new InvalidOperationException($"运行学习扫描超过单目录上限 {_options.MaxFilesPerRoot} 个文件，结果可能不完整。请缩小游戏安装目录或改用手动选择。");
                        if (files.Count >= _options.MaxTotalFiles)
                            throw new InvalidOperationException($"运行学习扫描超过总上限 {_options.MaxTotalFiles} 个文件，结果可能不完整。请改用手动选择存档目录。");
                        var info = new FileInfo(path);
                        string fullPath = Path.GetFullPath(path);
                        if (!seenFiles.Add(fullPath)) continue;
                        files.Add(new FileMetadataSnapshot(fullPath, info.Length, info.LastWriteTimeUtc));
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
        }
        return files;
    }

    private void ValidateOptions()
    {
        if (_options.MaxDepth < 0 || _options.MaxFilesPerRoot <= 0 || _options.MaxTotalFiles <= 0
            || _options.MaxCandidateDirectories <= 0 || _options.MaxScanSeconds <= 0)
            throw new ArgumentOutOfRangeException(nameof(_options), "运行学习扫描预算必须为正数。");
    }

    private void EnsureWithinBudget(Stopwatch stopwatch)
    {
        if (stopwatch.Elapsed > TimeSpan.FromSeconds(_options.MaxScanSeconds))
            throw new TimeoutException($"运行学习扫描超过 {_options.MaxScanSeconds} 秒安全时限，请改用手动选择或缩小候选范围。");
    }

    private static IReadOnlyList<string> NormalizeRoots(IEnumerable<string> roots)
    {
        string[] normalized = roots
            .Where(path => !string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(path => path.Length)
            .ToArray();
        var result = new List<string>();
        foreach (string candidate in normalized)
        {
            if (result.Any(parent => IsContainedBy(candidate, parent))) continue;
            result.Add(candidate);
        }
        return result;
    }

    private static bool IsContainedBy(string candidate, string parent)
    {
        string relative = Path.GetRelativePath(parent, candidate);
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal);
    }

    private static IReadOnlyList<(string Directory, int ChangeCount)> BuildCandidateDirectories(
        IReadOnlyList<FileMetadataSnapshot> changed)
    {
        string[] directories = changed
            .Select(file => Path.GetDirectoryName(file.FullPath))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => FindExistingCandidateDirectory(path!))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return directories.Select(directory => (
            directory,
            changed.Count(file => IsContainedBy(file.FullPath, directory)))).ToArray();
    }

    private static string NormalizeCandidateDirectory(string directory)
    {
        string current = Path.GetFullPath(directory);
        for (int depth = 0; depth < 2; depth++)
        {
            string leaf = Path.GetFileName(current).ToLowerInvariant();
            bool slotLike = leaf.StartsWith("slot", StringComparison.Ordinal)
                            || leaf.StartsWith("profile", StringComparison.Ordinal)
                            || (leaf.Length > 0 && leaf.All(char.IsDigit));
            if (!slotLike) break;
            string? parent = Path.GetDirectoryName(current);
            if (parent is null) break;
            current = parent;
        }
        return current;
    }

    private static string FindExistingCandidateDirectory(string directory)
    {
        string? current = Path.GetFullPath(directory);
        while (current is not null && !Directory.Exists(current))
            current = Path.GetDirectoryName(current);
        return NormalizeCandidateDirectory(current ?? directory);
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

    private static int Score(string directory, string installDirectory)
    {
        string value = directory.ToLowerInvariant();
        int score = 50;
        if (new[] { "save", "saved", "profile", "slot" }.Any(value.Contains)) score += 20;
        if (new[] { "cache", "temp", "log", "crash", "screenshot", "shader" }.Any(value.Contains)) score -= 60;
        string normalized = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
        string[] broadRoots =
        [
            installDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        ];
        if (broadRoots.Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(path => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)))
            .Any(path => string.Equals(path, normalized, StringComparison.OrdinalIgnoreCase)))
            score -= 40;
        return Math.Clamp(score, 0, 100);
    }
}
