using GameSaveManager.Application.Games;

namespace GameSaveManager.Application.Files;

/// <summary>在递归扫描前拒绝危险目录以及互相包含的存档根目录。</summary>
public static class SaveRootTopologyValidator
{
    public static void Validate(IReadOnlyList<SaveRootRule> roots, string? gameInstallDirectory = null)
    {
        if (roots.Count == 0) throw new InvalidOperationException("至少需要一个存档目录。");

        var normalized = roots.Select(root => (Rule: root, Path: Normalize(root.Path))).ToArray();
        foreach ((SaveRootRule rule, string path) in normalized)
        {
            ValidateProtectedDirectory(rule.RootId, path, gameInstallDirectory);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"存档目录不存在: {path}");
            ValidateNoReparsePointTraversal(path, rule.RootId);
        }

        for (int left = 0; left < normalized.Length; left++)
        {
            for (int right = left + 1; right < normalized.Length; right++)
            {
                if (Overlaps(normalized[left].Path, normalized[right].Path))
                    throw new InvalidOperationException(
                        $"存档目录不能相同或互相包含：{normalized[left].Rule.RootId} 与 {normalized[right].Rule.RootId}。");
            }
        }
    }

    private static void ValidateProtectedDirectory(string rootId, string path, string? gameInstallDirectory)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal)
            || (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.IsUnc))
            throw new InvalidOperationException($"{rootId} 不能选择网络 UNC 目录。");
        string? driveRoot = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(driveRoot) && Same(path, Normalize(driveRoot)))
            throw new InvalidOperationException($"{rootId} 不能选择磁盘根目录。");

        string windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (!string.IsNullOrWhiteSpace(windows))
        {
            windows = Normalize(windows);
            if (Same(path, windows) || IsAncestor(windows, path))
                throw new InvalidOperationException($"{rootId} 不能选择 Windows 系统目录或其子目录。");
        }

        foreach (string programFiles in GetProgramFilesRoots())
        {
            if (Same(path, programFiles))
                throw new InvalidOperationException($"{rootId} 不能选择 Program Files 根目录。");
            if (!IsAncestor(programFiles, path)) continue;

            string relative = Path.GetRelativePath(programFiles, path);
            int depth = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries).Length;
            if (depth < 3)
                throw new InvalidOperationException(
                    $"{rootId} 位于程序安装区域，必须选择至少三级深度的具体游戏存档目录。");

            if (!string.IsNullOrWhiteSpace(gameInstallDirectory))
            {
                string install = Normalize(gameInstallDirectory);
                if ((Same(install, programFiles) || IsAncestor(programFiles, install))
                    && (Same(path, install) || !IsAncestor(install, path)))
                    throw new InvalidOperationException(
                        $"{rootId} 位于 Program Files，但不是当前游戏安装目录下的具体存档子目录。");
            }
        }

        if (IsSteamLibraryContainer(path))
            throw new InvalidOperationException($"{rootId} 不能选择整个 Steam Library 或 steamapps 根目录。");

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Normalize(userProfile);
            if (Same(path, userProfile) || IsAncestor(path, userProfile))
                throw new InvalidOperationException($"{rootId} 不能选择整个用户主目录或它的上级目录。");
        }
    }

    public static bool IsInProgramFilesArea(string path)
    {
        string normalized = Normalize(path);
        return GetProgramFilesRoots().Any(root => Same(normalized, root) || IsAncestor(root, normalized));
    }

    public static void ValidateNoReparsePointTraversal(string path, string rootId = "存档目录")
    {
        DirectoryInfo? current = new(Path.GetFullPath(path));
        while (current is not null)
        {
            if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException(
                    $"{rootId} 的路径不能经过符号链接、Junction 或其他重解析点：{current.FullName}");
            current = current.Parent;
        }
    }

    private static IEnumerable<string> GetProgramFilesRoots() => new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Select(Normalize)
        .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsSteamLibraryContainer(string path)
    {
        string normalized = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        string leaf = Path.GetFileName(normalized);
        if (leaf.Equals("SteamLibrary", StringComparison.OrdinalIgnoreCase)
            || leaf.Equals("steamapps", StringComparison.OrdinalIgnoreCase)) return true;
        return leaf.Equals("common", StringComparison.OrdinalIgnoreCase)
            && string.Equals(Path.GetFileName(Path.GetDirectoryName(normalized)),
                "steamapps", StringComparison.OrdinalIgnoreCase);
    }

    private static bool Overlaps(string first, string second) =>
        Same(first, second) || IsAncestor(first, second) || IsAncestor(second, first);

    private static bool Same(string first, string second) =>
        string.Equals(first, second, StringComparison.OrdinalIgnoreCase);

    private static bool IsAncestor(string ancestor, string descendant) => descendant.StartsWith(
        ancestor.EndsWith(Path.DirectorySeparatorChar) ? ancestor : ancestor + Path.DirectorySeparatorChar,
        StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string path) =>
        Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
