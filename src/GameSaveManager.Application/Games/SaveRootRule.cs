namespace GameSaveManager.Application.Games;

using GameSaveManager.Application.Discovery;

/// <summary>一个已确认的存档根目录及其包含、排除规则。</summary>
public sealed record SaveRootRule(
    string RootId,
    string Path,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns,
    SaveLocationSource Source,
    int Confidence,
    bool UserConfirmed)
{
    public static SaveRootRule CreateDefault(string path, SaveLocationSource source, int confidence, bool userConfirmed) =>
        new("root", path, [], [], source, confidence, userConfirmed);
}
