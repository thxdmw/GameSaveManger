using System.Text;
using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.App;

internal static class UpdateHealthReporter
{
    public static bool TryReport(string[] arguments, out string? error)
    {
        error = null;
        string? file = GetOption(arguments, "--update-health-file");
        string? token = GetOption(arguments, "--update-health-token");
        if (file is null && token is null) return true;
        if (file is null || token is null || !Guid.TryParseExact(token, "N", out Guid parsedToken))
        {
            error = "更新启动确认参数不完整。";
            return false;
        }

        try
        {
            string transactionRoot = Path.GetFullPath(AppDataPaths.UpdateTransactionDirectory)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(file);
            string expectedFileName = $"health-{parsedToken:N}.ready";
            if (!fullPath.StartsWith(transactionRoot, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(Path.GetFileName(fullPath), expectedFileName, StringComparison.OrdinalIgnoreCase))
            {
                error = "更新启动确认文件不在受控目录中。";
                return false;
            }

            Directory.CreateDirectory(transactionRoot);
            string temporaryPath = fullPath + ".tmp";
            File.WriteAllText(temporaryPath, token, new UTF8Encoding(false));
            File.Move(temporaryPath, fullPath, true);
            return true;
        }
        catch (Exception exception)
        {
            error = exception.Message;
            return false;
        }
    }

    public static void CleanupStaleArtifacts()
    {
        try
        {
            var threshold = DateTime.UtcNow.AddDays(-7);
            if (Directory.Exists(AppDataPaths.UpdateTransactionDirectory)
                && IsSafeManagedDirectory(AppDataPaths.UpdateTransactionDirectory))
            {
                foreach (string path in Directory.EnumerateFiles(AppDataPaths.UpdateTransactionDirectory))
                {
                    string name = Path.GetFileName(path);
                    bool disposable = name.StartsWith("bootstrapper-", StringComparison.OrdinalIgnoreCase) ||
                                      name.StartsWith("health-", StringComparison.OrdinalIgnoreCase) ||
                                      name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase);
                    if (disposable && File.GetLastWriteTimeUtc(path) < threshold)
                    {
                        try { File.Delete(path); } catch (IOException) { }
                        catch (UnauthorizedAccessException) { }
                    }
                }
            }

            if (Directory.Exists(AppDataPaths.RollbackInstallerDirectory)
                && IsSafeManagedDirectory(AppDataPaths.RollbackInstallerDirectory))
            {
                foreach (FileInfo oldInstaller in new DirectoryInfo(AppDataPaths.RollbackInstallerDirectory)
                             .EnumerateFiles("GameSaveManager-Setup-*.exe")
                             .OrderByDescending(file => file.LastWriteTimeUtc)
                             .Skip(3))
                {
                    try { oldInstaller.Delete(); } catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }

            if (Directory.Exists(AppDataPaths.UpdateDirectory)
                && IsSafeManagedDirectory(AppDataPaths.UpdateDirectory))
            {
                foreach (DirectoryInfo oldVersion in new DirectoryInfo(AppDataPaths.UpdateDirectory)
                             .EnumerateDirectories()
                             .Where(directory => !string.Equals(directory.FullName, AppDataPaths.UpdateTransactionDirectory, StringComparison.OrdinalIgnoreCase))
                             .Where(directory => (directory.Attributes & FileAttributes.ReparsePoint) == 0)
                             .OrderByDescending(directory => directory.LastWriteTimeUtc)
                             .Skip(2)
                             .Where(directory => directory.LastWriteTimeUtc < threshold))
                {
                    try { oldVersion.Delete(recursive: true); }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static bool IsSafeManagedDirectory(string path)
    {
        try
        {
            string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppDataPaths.RootDirectory));
            string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
            string relative = Path.GetRelativePath(boundary, target);
            if (Path.IsPathRooted(relative)
                || relative.Equals("..", StringComparison.Ordinal)
                || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return false;

            string current = boundary;
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

    private static string? GetOption(string[] arguments, string name)
    {
        for (int index = 0; index < arguments.Length - 1; index++)
            if (string.Equals(arguments[index], name, StringComparison.OrdinalIgnoreCase))
                return arguments[index + 1];
        return null;
    }
}
