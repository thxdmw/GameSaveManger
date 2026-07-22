using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>优先脱敏用户目录；无法模板化时保留绝对路径作为仅当前账号可见的参考。</summary>
public sealed class WindowsSavePathTemplateService : ISavePathTemplateService
{
    private static readonly IReadOnlyList<(string Token, Func<string> Resolve)> Tokens =
    [
        ("%DOCUMENTS%", () => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
        ("%SAVEDGAMES%", () => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games")),
        ("%LOCALAPPDATA%", () => Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),
        ("%APPDATA%", () => Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)),
        ("%USERPROFILE%", () => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile))
    ];

    public string Encode(string path)
    {
        string fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        foreach ((string token, Func<string> resolver) in Tokens
                     .Select(item => (item.Token, item.Resolve, Value: Normalize(item.Resolve())))
                     .Where(item => item.Value.Length > 0)
                     .OrderByDescending(item => item.Value.Length)
                     .Select(item => (item.Token, item.Resolve)))
        {
            string root = Normalize(resolver());
            if (fullPath.Equals(root, StringComparison.OrdinalIgnoreCase)) return token;
            if (fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return token + fullPath[root.Length..];
        }
        return fullPath;
    }

    public string? Resolve(string pathTemplate)
    {
        if (string.IsNullOrWhiteSpace(pathTemplate)) return null;
        try
        {
            string value = pathTemplate.Trim();
            foreach ((string token, Func<string> resolver) in Tokens)
            {
                if (!value.StartsWith(token, StringComparison.OrdinalIgnoreCase)) continue;
                string remainder = value[token.Length..];
                if (remainder.Length > 0
                    && remainder[0] != Path.DirectorySeparatorChar
                    && remainder[0] != Path.AltDirectorySeparatorChar)
                    return null;
                string resolvedRoot = resolver();
                if (string.IsNullOrWhiteSpace(resolvedRoot)) return null;
                string root = Path.GetFullPath(resolvedRoot);
                string suffix = remainder.TrimStart(
                    Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                string candidate = Path.GetFullPath(Path.Combine(root, suffix));
                string relative = Path.GetRelativePath(root, candidate);
                if (Path.IsPathRooted(relative)
                    || relative.Equals("..", StringComparison.Ordinal)
                    || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                    || relative.StartsWith(".." + Path.AltDirectorySeparatorChar, StringComparison.Ordinal))
                    return null;
                return candidate;
            }
            return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null;
        }
        catch (Exception exception) when (exception is ArgumentException
                                                   or NotSupportedException
                                                   or IOException)
        {
            return null;
        }
    }

    private static string Normalize(string path) => string.IsNullOrWhiteSpace(path)
        ? string.Empty
        : Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
}
