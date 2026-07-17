using GameSaveManager.Application.Games;

namespace GameSaveManager.Application.Files;

/// <summary>在递归扫描前拒绝危险目录以及互相包含的存档根目录。</summary>
public static class SaveRootTopologyValidator
{
    public static void Validate(IReadOnlyList<SaveRootRule> roots)
    {
        if (roots.Count == 0) throw new InvalidOperationException("至少需要一个存档目录。");

        var normalized = roots.Select(root => (Rule: root, Path: Normalize(root.Path))).ToArray();
        foreach ((SaveRootRule rule, string path) in normalized)
        {
            ValidateProtectedDirectory(rule.RootId, path);
            if (!Directory.Exists(path)) throw new DirectoryNotFoundException($"存档目录不存在: {path}");
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

    private static void ValidateProtectedDirectory(string rootId, string path)
    {
        if (Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) && uri.IsUnc)
            throw new InvalidOperationException($"{rootId} 不能选择网络 UNC 目录。");
        string? driveRoot = Path.GetPathRoot(path);
        if (!string.IsNullOrWhiteSpace(driveRoot) && Same(path, Normalize(driveRoot)))
            throw new InvalidOperationException($"{rootId} 不能选择磁盘根目录。");

        foreach (string protectedPath in new[]
                 {
                     Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                     Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
                 }.Where(value => !string.IsNullOrWhiteSpace(value)).Select(Normalize))
        {
            if (Same(path, protectedPath) || IsAncestor(protectedPath, path))
                throw new InvalidOperationException($"{rootId} 不能选择 Windows 或 Program Files 系统目录。");
        }

        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            userProfile = Normalize(userProfile);
            if (Same(path, userProfile) || IsAncestor(path, userProfile))
                throw new InvalidOperationException($"{rootId} 不能选择整个用户主目录或它的上级目录。");
        }
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
