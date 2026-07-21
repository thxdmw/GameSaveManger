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
        string fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
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
        string value = pathTemplate.Trim();
        foreach ((string token, Func<string> resolver) in Tokens)
        {
            if (!value.StartsWith(token, StringComparison.OrdinalIgnoreCase)) continue;
            string suffix = value[token.Length..].TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return Path.GetFullPath(Path.Combine(resolver(), suffix));
        }
        return Path.IsPathFullyQualified(value) ? Path.GetFullPath(value) : null;
    }

    private static string Normalize(string path) => string.IsNullOrWhiteSpace(path)
        ? string.Empty
        : Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
