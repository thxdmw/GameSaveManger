using GameSaveManager.Application.Games;

namespace GameSaveManager.Application.Files;

using System.Security.Cryptography;
using System.Text;

public sealed record SaveDirectoryPreview(int FileCount, long TotalSize, DateTime? LatestWriteTimeUtc,
    IReadOnlyList<string> RecentFiles, IReadOnlyList<string> LargestFiles, IReadOnlyList<string> Warnings,
    IReadOnlyList<string> AppliedIncludes, IReadOnlyList<string> AppliedExcludes);

public sealed record SaveRootPreview(
    SaveRootRule Rule,
    int FileCount,
    long TotalSize,
    DateTime? LatestWriteTimeUtc,
    IReadOnlyList<string> Warnings)
{
    public string Summary => $"{FileCount} 个文件，{FormatBytes(TotalSize)}"
        + (Warnings.Count == 0 ? string.Empty : $"；{string.Join("；", Warnings)}");

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }
}

public sealed record SaveProfilePreview(
    IReadOnlyList<SaveRootPreview> Roots,
    int TotalFiles,
    long TotalSize,
    DateTime? LatestWriteTimeUtc,
    IReadOnlyList<string> Warnings,
    string Fingerprint);

public static class SaveProfileFingerprint
{
    public static string Create(
        IReadOnlyList<SaveRootRule> roots,
        IReadOnlyList<RegistrySaveRule> registryRules)
    {
        IEnumerable<string> rootParts = roots
            .OrderBy(rule => rule.RootId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.Path, StringComparer.OrdinalIgnoreCase)
            .Select(rule => string.Join("|",
                "root",
                rule.RootId.Trim().ToUpperInvariant(),
                NormalizePath(rule.Path),
                rule.Source.ToString().ToUpperInvariant(),
                string.Join(";", rule.IncludePatterns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Select(value => value.Trim().ToUpperInvariant())),
                string.Join(";", rule.ExcludePatterns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                    .Select(value => value.Trim().ToUpperInvariant()))));
        IEnumerable<string> registryParts = registryRules
            .OrderBy(rule => rule.RuleId, StringComparer.OrdinalIgnoreCase)
            .ThenBy(rule => rule.KeyPath, StringComparer.OrdinalIgnoreCase)
            .Select(rule => string.Join("|", "registry", rule.RuleId.Trim().ToUpperInvariant(),
                rule.KeyPath.Trim().Replace('/', '\\').ToUpperInvariant()));
        string canonical = string.Join("\n", rootParts.Concat(registryParts));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }

    private static string NormalizePath(string path) => Path.GetFullPath(path)
        .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
        .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
        .ToUpperInvariant();
}

public interface ISaveDirectoryPreviewService
{
    Task<SaveDirectoryPreview> PreviewAsync(SaveRootRule rule, CancellationToken cancellationToken);
    Task<SaveProfilePreview> PreviewProfileAsync(
        IReadOnlyList<SaveRootRule> roots,
        IReadOnlyList<RegistrySaveRule> registryRules,
        CancellationToken cancellationToken);
}
