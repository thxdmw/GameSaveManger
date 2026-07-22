using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.Infrastructure.Maintenance;

public sealed record LocalStorageMaintenanceResult(long BeforeBytes, long AfterBytes, int DeletedFiles, int DeletedDirectories)
{
    public long FreedBytes => Math.Max(0, BeforeBytes - AfterBytes);
}

/// <summary>只清理可重新下载的对象缓存、下载残片和旧更新包；不会触碰数据库、日志或游戏原始存档。</summary>
public sealed class LocalStorageMaintenanceService(string? applicationRoot = null)
{
    private readonly string _applicationRoot = Path.GetFullPath(applicationRoot ?? AppDataPaths.RootDirectory);

    public Task<long> GetManagedUsageAsync(CancellationToken cancellationToken) =>
        Task.Run(() => CalculateUsage(cancellationToken), cancellationToken);

    public Task<LocalStorageMaintenanceResult> CleanupAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Cleanup(cancellationToken), cancellationToken);

    private LocalStorageMaintenanceResult Cleanup(CancellationToken cancellationToken)
    {
        long before = CalculateUsage(cancellationToken);
        int deletedFiles = 0;
        int deletedDirectories = 0;
        string objectsRoot = Path.Combine(_applicationRoot, "objects");
        if (Directory.Exists(objectsRoot) && IsSafeManagedRoot(objectsRoot))
        {
            DateTime partialThreshold = DateTime.UtcNow.AddDays(-1);
            foreach (FileInfo partial in EnumerateFilesSafe(objectsRoot)
                         .Where(file => file.Name.Contains(".partial-", StringComparison.OrdinalIgnoreCase)
                                        && file.LastWriteTimeUtc < partialThreshold))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (TryDeleteFile(partial)) deletedFiles++;
            }

            FileInfo[] objects = EnumerateFilesSafe(objectsRoot)
                .Where(file => file.Name.Length == 64 && file.Name.All(Uri.IsHexDigit))
                .OrderByDescending(file => SafeLastAccessTimeUtc(file))
                .ToArray();
            const long maximumCacheBytes = 5L * 1024 * 1024 * 1024;
            long retainedBytes = 0;
            DateTime staleThreshold = DateTime.UtcNow.AddDays(-30);
            foreach (FileInfo file in objects)
            {
                cancellationToken.ThrowIfCancellationRequested();
                bool stale = SafeLastAccessTimeUtc(file) < staleThreshold;
                bool overBudget = retainedBytes + file.Length > maximumCacheBytes;
                if ((stale || overBudget) && TryDeleteFile(file))
                {
                    deletedFiles++;
                    continue;
                }
                retainedBytes += file.Length;
            }
            deletedDirectories += DeleteEmptyDirectories(objectsRoot, cancellationToken);
        }

        string updateRoot = Path.Combine(_applicationRoot, "updates");
        if (Directory.Exists(updateRoot) && IsSafeManagedRoot(updateRoot))
        {
            DirectoryInfo[] oldVersions = new DirectoryInfo(updateRoot)
                .EnumerateDirectories()
                .Where(directory => !string.Equals(directory.Name, "transactions", StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(directory => directory.LastWriteTimeUtc)
                .Skip(2)
                .Where(directory => directory.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7))
                .ToArray();
            foreach (DirectoryInfo directory in oldVersions)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if (TryDeleteDirectory(directory)) deletedDirectories++;
            }
        }

        long after = CalculateUsage(cancellationToken);
        return new LocalStorageMaintenanceResult(before, after, deletedFiles, deletedDirectories);
    }

    private long CalculateUsage(CancellationToken cancellationToken)
    {
        long total = 0;
        foreach (string relative in new[] { "objects", "updates", "rollback", "restore" })
        {
            string path = Path.Combine(_applicationRoot, relative);
            if (!Directory.Exists(path) || !IsSafeManagedRoot(path)) continue;
            foreach (FileInfo file in EnumerateFilesSafe(path))
            {
                cancellationToken.ThrowIfCancellationRequested();
                try { total = checked(total + file.Length); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
        return total;
    }

    private bool IsSafeManagedRoot(string path)
    {
        try
        {
            string root = Path.TrimEndingDirectorySeparator(_applicationRoot);
            string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string relative = Path.GetRelativePath(root, target);
            if (Path.IsPathRooted(relative)
                || relative is "." or ".."
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return false;

            string current = root;
            foreach (string segment in relative.Split(
                         [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                         StringSplitOptions.RemoveEmptyEntries).Prepend(string.Empty))
            {
                if (segment.Length > 0) current = Path.Combine(current, segment);
                if (Directory.Exists(current)
                    && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return false;
            }
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    private static IEnumerable<FileInfo> EnumerateFilesSafe(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            IEnumerable<string> files;
            IEnumerable<string> directories;
            try
            {
                files = Directory.EnumerateFiles(current).ToArray();
                directories = Directory.EnumerateDirectories(current).ToArray();
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                continue;
            }
            foreach (string file in files) yield return new FileInfo(file);
            foreach (string directory in directories)
            {
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) == 0) pending.Enqueue(directory);
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
            }
        }
    }

    private static DateTime SafeLastAccessTimeUtc(FileInfo file)
    {
        try { return file.LastAccessTimeUtc; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return file.LastWriteTimeUtc; }
    }

    private static bool TryDeleteFile(FileInfo file)
    {
        try { file.Delete(); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static bool TryDeleteDirectory(DirectoryInfo directory)
    {
        try { directory.Delete(recursive: true); return true; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return false; }
    }

    private static int DeleteEmptyDirectories(string root, CancellationToken cancellationToken)
    {
        int deleted = 0;
        DirectoryInfo[] directories = EnumerateDirectoriesSafe(root)
            .OrderByDescending(directory => directory.FullName.Length)
            .ToArray();
        foreach (DirectoryInfo directory in directories)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!directory.EnumerateFileSystemInfos().Any())
                {
                    directory.Delete();
                    deleted++;
                }
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
        }
        return deleted;
    }

    private static IEnumerable<DirectoryInfo> EnumerateDirectoriesSafe(string root)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);
        while (pending.Count > 0)
        {
            string current = pending.Dequeue();
            string[] children;
            try { children = Directory.GetDirectories(current); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
            foreach (string child in children)
            {
                FileAttributes attributes;
                try { attributes = File.GetAttributes(child); }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { continue; }
                if ((attributes & FileAttributes.ReparsePoint) != 0) continue;
                yield return new DirectoryInfo(child);
                pending.Enqueue(child);
            }
        }
    }
}
