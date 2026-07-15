using System.Text.RegularExpressions;
using GameSaveManager.Application.Games;

namespace GameSaveManager.Infrastructure.FileSystem;

internal static class SaveRuleMatcher
{
    public static void Validate(SaveRootRule rule)
    {
        if (string.IsNullOrWhiteSpace(rule.RootId) || !Regex.IsMatch(rule.RootId, "^[a-zA-Z0-9_-]{1,64}$")) throw new InvalidOperationException("存档根目录标识只能包含字母、数字、下划线和连字符。");
        if (string.IsNullOrWhiteSpace(rule.Path) || !Path.IsPathRooted(rule.Path)) throw new InvalidOperationException("存档根目录必须是绝对路径。");
        foreach (string pattern in rule.IncludePatterns.Concat(rule.ExcludePatterns))
        {
            if (string.IsNullOrWhiteSpace(pattern) || Path.IsPathRooted(pattern) || pattern.Contains("..", StringComparison.Ordinal)) throw new InvalidOperationException("存档规则必须是根目录内的相对 Glob，且不能包含 ..。");
        }
    }

    public static bool Includes(SaveRootRule rule, string relativePath)
    {
        string normalized = relativePath.Replace('\\', '/');
        bool included = rule.IncludePatterns.Count == 0 || rule.IncludePatterns.Any(pattern => Matches(pattern, normalized));
        return included && !rule.ExcludePatterns.Any(pattern => Matches(pattern.TrimStart('!'), normalized));
    }

    private static bool Matches(string pattern, string path)
    {
        string expression = "^" + Regex.Escape(pattern.Replace('\\', '/'))
            .Replace("\\*\\*", ".*")
            .Replace("\\*", "[^/]*")
            .Replace("\\?", "[^/]") + "$";
        return Regex.IsMatch(path, expression, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
