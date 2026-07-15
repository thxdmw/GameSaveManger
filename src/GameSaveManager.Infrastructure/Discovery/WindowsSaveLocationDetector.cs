using System.Diagnostics;
using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>并行运行多种 Windows 存档检测器，并在单个检测器失败时隔离错误。</summary>
public sealed class WindowsSaveLocationDetector : ISaveLocationDetector
{
    private static readonly string[] InstallNames = ["save", "saves", "SaveData", "Saved", "Saved\\SaveGames", "userdata", "user", "profile", "profiles", "data\\save", "data\\saves"];
    private static readonly string[] SaveTerms = ["save", "saved", "profile", "userdata", "slot"];
    private static readonly string[] ExcludedTerms = ["cache", "temp", "logs", "log", "crash", "screenshots", "shadercache", "mods", "workshop", "redist", "support", "installer"];

    public async Task<IReadOnlyList<SaveLocationCandidate>> DetectAsync(GameIdentity game, IProgress<SaveDetectionProgress>? progress, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(game.Name);
        progress?.Report(new SaveDetectionProgress("开始", "正在查找存档候选目录…", 0, 4));
        Task<IReadOnlyList<SaveLocationCandidate>> manifest = RunDetectorAsync("ludusavi", () => LudusaviManifestDetector.Detect(game, cancellationToken), cancellationToken);
        Task<IReadOnlyList<SaveLocationCandidate>> install = RunDetectorAsync("install", () => DetectInstallDirectory(game, cancellationToken), cancellationToken);
        Task<IReadOnlyList<SaveLocationCandidate>> common = RunDetectorAsync("common", () => DetectCommonDirectories(game, cancellationToken), cancellationToken);
        Task<IReadOnlyList<SaveLocationCandidate>> steam = RunDetectorAsync("steam-userdata", () => DetectSteamUserdata(game, cancellationToken), cancellationToken);
        IReadOnlyList<SaveLocationCandidate>[] groups = await Task.WhenAll(manifest, install, common, steam);
        progress?.Report(new SaveDetectionProgress("合并", "正在整理候选目录…", 3, 4));
        IReadOnlyList<SaveLocationCandidate> result = groups.SelectMany(group => group)
            .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(candidate => candidate.Confidence).ThenByDescending(candidate => candidate.LatestWriteTimeUtc).First())
            .OrderByDescending(candidate => candidate.Confidence).ThenByDescending(candidate => candidate.LatestWriteTimeUtc).ToArray();
        progress?.Report(new SaveDetectionProgress("完成", $"完成：找到 {result.Count} 个候选目录，仍需用户确认。", 4, 4));
        return result;
    }

    private static async Task<IReadOnlyList<SaveLocationCandidate>> RunDetectorAsync(string detector, Func<IReadOnlyList<SaveLocationCandidate>> action, CancellationToken cancellationToken)
    {
        try { return await Task.Run(action, cancellationToken); }
        catch (OperationCanceledException) { throw; }
        catch (Exception exception)
        {
            Trace.TraceWarning($"存档检测器 {detector} 失败：{exception.GetType().Name}。\n{exception.Message}");
            return [];
        }
    }
    private static IReadOnlyList<SaveLocationCandidate> DetectInstallDirectory(GameIdentity game, CancellationToken cancellationToken)
    {
        var results = new List<SaveLocationCandidate>();
        if (string.IsNullOrWhiteSpace(game.InstallDirectory) || !Directory.Exists(game.InstallDirectory)) return results;
        foreach (string name in InstallNames)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SaveLocationCandidate? candidate = SaveLocationCandidateFactory.Create(Path.Combine(game.InstallDirectory, name), 70,
                SaveLocationSource.InstallDirectory, $"命中安装目录常见规则：{name}");
            if (candidate is not null) results.Add(candidate);
        }
        try
        {
            foreach (string directory in Directory.EnumerateDirectories(game.InstallDirectory, "*", SearchOption.AllDirectories))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string relative = Path.GetRelativePath(game.InstallDirectory, directory);
                if (relative.Count(character => character == Path.DirectorySeparatorChar || character == Path.AltDirectorySeparatorChar) >= 4) continue;
                string leaf = Path.GetFileName(directory);
                if (ExcludedTerms.Any(term => leaf.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                if (!SaveTerms.Any(term => leaf.Contains(term, StringComparison.OrdinalIgnoreCase))) continue;
                SaveLocationCandidate? candidate = SaveLocationCandidateFactory.Create(directory, 55, SaveLocationSource.InstallDirectory,
                    $"安装目录下的存档关键词目录：{leaf}");
                if (candidate is not null) results.Add(candidate);
            }
        }
        catch (UnauthorizedAccessException exception) { Trace.TraceWarning($"安装目录检测被拒绝：{exception.Message}"); }
        catch (IOException exception) { Trace.TraceWarning($"安装目录检测失败：{exception.Message}"); }
        return results;
    }

    private static IReadOnlyList<SaveLocationCandidate> DetectCommonDirectories(GameIdentity game, CancellationToken cancellationToken)
    {
        string name = game.Name.Trim();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string savedGames = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Saved Games");
        string localLow = Path.Combine(local, "..", "LocalLow");
        string[] paths = [Path.Combine(documents, "My Games", name), Path.Combine(documents, name), Path.Combine(savedGames, name), Path.Combine(local, name), Path.Combine(roaming, name), Path.Combine(localLow, name)];
        return paths.Select(path =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            return SaveLocationCandidateFactory.Create(path, 45, SaveLocationSource.Heuristic, "命中常见 Windows 用户目录规则");
        }).Where(candidate => candidate is not null).Cast<SaveLocationCandidate>().ToArray();
    }

    private static IReadOnlyList<SaveLocationCandidate> DetectSteamUserdata(GameIdentity game, CancellationToken cancellationToken)
    {
        if (!string.Equals(game.Provider, GameIdentity.Steam, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(game.ProviderGameId)) return [];
        string? steamRoot = FindSteamRoot(game.InstallDirectory);
        if (steamRoot is null) return [];
        string userdata = Path.Combine(steamRoot, "userdata");
        if (!Directory.Exists(userdata)) return [];
        var results = new List<SaveLocationCandidate>();
        foreach (string userDirectory in Directory.EnumerateDirectories(userdata))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string appDirectory = Path.Combine(userDirectory, game.ProviderGameId);
            foreach (string name in new[] { "remote", "local", "save", "saves", "profile" })
            {
                SaveLocationCandidate? candidate = SaveLocationCandidateFactory.Create(Path.Combine(appDirectory, name), name == "remote" ? 90 : 80,
                    SaveLocationSource.StoreMetadata, $"Steam userdata/{Path.GetFileName(userDirectory)}/{game.ProviderGameId}/{name}");
                if (candidate is not null) results.Add(candidate);
            }
        }
        return results;
    }

    private static string? FindSteamRoot(string installDirectory)
    {
        DirectoryInfo? current = new(installDirectory);
        while (current is not null)
        {
            if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) return current.Parent?.FullName;
            current = current.Parent;
        }
        return null;
    }
}
