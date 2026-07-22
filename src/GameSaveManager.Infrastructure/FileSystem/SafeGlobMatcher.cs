using System.Text;
using System.Text.RegularExpressions;

namespace GameSaveManager.Infrastructure.FileSystem;

/// <summary>受限的相对路径 Glob 编译器；不允许模式逃逸存档根目录。</summary>
internal static class SafeGlobMatcher
{
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(1);

    public static Regex Compile(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) throw new InvalidOperationException("Glob 规则不能为空。");
        string normalized = NormalizePattern(pattern);
        var expression = new StringBuilder("^");
        for (int index = 0; index < normalized.Length; index++)
        {
            char current = normalized[index];
            if (current == '*' && index + 2 < normalized.Length && normalized[index + 1] == '*' && normalized[index + 2] == '/')
            {
                expression.Append("(?:.*/)?");
                index += 2;
            }
            else if (current == '*' && index + 1 < normalized.Length && normalized[index + 1] == '*')
            {
                expression.Append(".*");
                index++;
            }
            else if (current == '*') expression.Append("[^/]*");
            else if (current == '?') expression.Append("[^/]");
            else expression.Append(Regex.Escape(current.ToString()));
        }
        expression.Append('$');
        return new Regex(expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, RegexTimeout);
    }

    public static bool IsMatch(string pattern, string normalizedRelativePath)
    {
        string path = NormalizeRelativePath(normalizedRelativePath);
        return Compile(pattern).IsMatch(path);
    }

    public static string NormalizeRelativePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || Path.IsPathRooted(path)) throw new InvalidOperationException("存档相对路径必须位于根目录内。");
        string normalized = path.Replace('\\', '/').TrimStart('/');
        if (normalized.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidOperationException("存档相对路径不能包含空段、. 或 ..。");
        return normalized;
    }

    public static string NormalizePattern(string pattern)
    {
        if (Path.IsPathRooted(pattern)) throw new InvalidOperationException("Glob 规则必须是相对路径。");
        string normalized = pattern.Trim().Replace('\\', '/').TrimStart('!').TrimStart('/');
        if (normalized.Split('/').Any(segment => segment is "" or "." or "..")) throw new InvalidOperationException("Glob 规则不能包含空段、. 或 ..。");
        return normalized;
    }
}
