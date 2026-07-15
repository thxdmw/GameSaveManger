using System.Text.RegularExpressions;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Infrastructure.Persistence;
using YamlDotNet.RepresentationModel;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>从 Ludusavi Manifest 按平台 ID 或别名推导 Windows 存档目录。</summary>
internal static class LudusaviManifestDetector
{
    private static Lazy<ManifestIndex> _index = new(LoadIndex, LazyThreadSafetyMode.ExecutionAndPublication);
    private static readonly Regex Placeholder = new("<(?<name>[^>]+)>", RegexOptions.Compiled);

    public static IReadOnlyList<SaveLocationCandidate> Detect(GameIdentity game, CancellationToken cancellationToken)
    {
        ManifestEntry? entry = _index.Value.Find(game);
        if (entry is null) return [];
        var candidates = new List<SaveLocationCandidate>();
        foreach (string rule in entry.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? directory = ResolveDirectory(rule, game);
            if (directory is null) continue;
            SaveLocationCandidate? candidate = SaveLocationCandidateFactory.Create(directory, entry.MatchConfidence,
                SaveLocationSource.LudusaviManifest, $"Ludusavi Manifest：{entry.Name}");
            if (candidate is not null) candidates.Add(candidate);
        }
        return candidates;
    }

    internal static void ValidateManifestFile(string path)
    {
        var stream = new YamlStream();
        using var reader = new StreamReader(path);
        stream.Load(reader);
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root || root.Children.Count == 0)
            throw new InvalidDataException("Ludusavi Manifest 根节点必须包含游戏映射。");
        int usableEntries = root.Children.Count(pair => pair.Key is YamlScalarNode { Value: { Length: > 0 } }
            && pair.Value is YamlMappingNode data
            && (data.Children.ContainsKey(new YamlScalarNode("files")) || data.Children.ContainsKey(new YamlScalarNode("steam")) || data.Children.ContainsKey(new YamlScalarNode("gog"))));
        if (usableEntries < 10) throw new InvalidDataException("Ludusavi Manifest 有效游戏条目数量不足。");
    }
    internal static void Invalidate() => _index = new Lazy<ManifestIndex>(LoadIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    private static ManifestIndex LoadIndex()
    {
        string installedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ludusavi-manifest.yaml");
        string updatedPath = Path.Combine(AppDataPaths.RootDirectory, "manifest", "ludusavi-manifest.yaml");
        string path = File.Exists(updatedPath) ? updatedPath : installedPath;
        if (!File.Exists(path)) return new ManifestIndex([]);
        using var reader = new StreamReader(path);
        var stream = new YamlStream();
        stream.Load(reader);
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root) return new ManifestIndex([]);
        var entries = new List<ManifestEntry>();
        foreach ((YamlNode nameNode, YamlNode dataNode) in root.Children)
        {
            if (nameNode is not YamlScalarNode { Value: { } name } || dataNode is not YamlMappingNode data) continue;
            string? steam = Scalar(data, "steam", "id");
            string? gog = Scalar(data, "gog", "id");
            string? aliasTarget = Scalar(data, "alias");
            var aliases = Sequence(data, "alias").Append(name).ToArray();
            var files = MappingKeys(data, "files").Where(path => IsRuleAllowed(data, path)).ToArray();
            if (files.Length > 0 || aliasTarget is not null) entries.Add(new ManifestEntry(name, steam, gog, aliases, files, aliasTarget));
        }
        return new ManifestIndex(entries);
    }

    private static bool IsRuleAllowed(YamlMappingNode data, string path)
    {
        if (!data.Children.TryGetValue(new YamlScalarNode("files"), out YamlNode? files) || files is not YamlMappingNode mapping || !mapping.Children.TryGetValue(new YamlScalarNode(path), out YamlNode? node) || node is not YamlMappingNode file) return true;
        if (!file.Children.TryGetValue(new YamlScalarNode("when"), out YamlNode? when) || when is not YamlMappingNode constraints) return true;
        string? os = (constraints.Children.TryGetValue(new YamlScalarNode("os"), out YamlNode? osNode) ? (osNode as YamlScalarNode)?.Value : null);
        if (!string.IsNullOrWhiteSpace(os) && !os.Split(',', StringSplitOptions.TrimEntries).Contains("windows", StringComparer.OrdinalIgnoreCase)) return false;
        return true;
    }

    private static string? ResolveDirectory(string rule, GameIdentity game)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ["root"] = FindStoreRoot(game.InstallDirectory) ?? string.Empty,
            ["game"] = Path.GetFileName(game.InstallDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)), ["base"] = game.InstallDirectory, ["storeGameId"] = game.ProviderGameId ?? string.Empty,
            ["storeUserId"] = string.Empty, ["osUserName"] = Environment.UserName, ["winDocuments"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["winAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ["winLocalAppData"] = local,
            ["winLocalAppDataLow"] = Path.Combine(local, "..", "LocalLow"), ["winPublic"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments),
            ["winProgramData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), ["winDir"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };
        string expanded = Placeholder.Replace(rule.Replace('/', Path.DirectorySeparatorChar), match => values.TryGetValue(match.Groups["name"].Value, out string? value) ? value : match.Value);
        if (expanded.Contains("..", StringComparison.Ordinal) || Placeholder.IsMatch(expanded)) return null;
        int wildcard = expanded.IndexOfAny(['*', '?']);
        string directory = wildcard >= 0 ? expanded[..wildcard].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) : Path.GetDirectoryName(expanded) ?? expanded;
        try { return Path.GetFullPath(directory); }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException) { return null; }
    }

    private static string? FindStoreRoot(string installDirectory)
    {
        DirectoryInfo? current = new(installDirectory);
        while (current is not null) { if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) return current.Parent?.FullName; current = current.Parent; }
        return null;
    }

    private static string? Scalar(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) ? (value as YamlScalarNode)?.Value : null;
    private static string? Scalar(YamlMappingNode node, string parent, string child) => node.Children.TryGetValue(new YamlScalarNode(parent), out YamlNode? value) && value is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(child), out YamlNode? scalar) ? (scalar as YamlScalarNode)?.Value : null;
    private static IEnumerable<string> Sequence(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlSequenceNode sequence ? sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value!).Where(item => !string.IsNullOrWhiteSpace(item)) : [];
    private static IEnumerable<string> MappingKeys(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlMappingNode mapping ? mapping.Children.Keys.OfType<YamlScalarNode>().Select(item => item.Value!).Where(item => !string.IsNullOrWhiteSpace(item)) : [];

    private sealed record ManifestEntry(string Name, string? SteamId, string? GogId, IReadOnlyList<string> Aliases, IReadOnlyList<string> Files, string? AliasTarget)
    {
        public int MatchConfidence { get; private set; }
        public bool Matches(GameIdentity game)
        {
            if (string.Equals(game.Provider, GameIdentity.Steam, StringComparison.OrdinalIgnoreCase) && string.Equals(game.ProviderGameId, SteamId, StringComparison.OrdinalIgnoreCase)) { MatchConfidence = 100; return true; }
            if (string.Equals(game.Provider, GameIdentity.Gog, StringComparison.OrdinalIgnoreCase) && string.Equals(game.ProviderGameId, GogId, StringComparison.OrdinalIgnoreCase)) { MatchConfidence = 100; return true; }
            if (Aliases.Any(alias => string.Equals(Normalize(alias), Normalize(game.Name), StringComparison.Ordinal))) { MatchConfidence = 85; return true; }
            return false;
        }
    }

    private sealed class ManifestIndex(IReadOnlyList<ManifestEntry> entries)
    {
        public ManifestEntry? Find(GameIdentity game)
        {
            ManifestEntry? entry = entries.FirstOrDefault(item => item.Matches(game));
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (entry?.AliasTarget is { Length: > 0 } target && visited.Add(entry.Name)) entry = entries.FirstOrDefault(item => string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase));
            return entry;
        }
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), "[\\s:_'?\\-]", string.Empty)
        .Replace("gameoftheyear", string.Empty).Replace("goty", string.Empty).Replace("definitiveedition", string.Empty).Replace("remastered", string.Empty).Replace("deluxeedition", string.Empty);
}
