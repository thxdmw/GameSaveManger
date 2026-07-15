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
            var aliases = Sequence(data, "alias").Append(name).ToArray();
            var files = MappingKeys(data, "files").Where(path => IsWindowsRule(data, path)).ToArray();
            if (files.Length > 0) entries.Add(new ManifestEntry(name, steam, gog, aliases, files));
        }
        return new ManifestIndex(entries);
    }

    private static bool IsWindowsRule(YamlMappingNode data, string path)
    {
        if (!data.Children.TryGetValue(new YamlScalarNode("files"), out YamlNode? files) || files is not YamlMappingNode mapping || !mapping.Children.TryGetValue(new YamlScalarNode(path), out YamlNode? node) || node is not YamlMappingNode file) return true;
        if (!file.Children.TryGetValue(new YamlScalarNode("when"), out YamlNode? when)) return true;
        return when.ToString().Contains("windows", StringComparison.OrdinalIgnoreCase) || !when.ToString().Contains("os", StringComparison.OrdinalIgnoreCase);
    }

    private static string? ResolveDirectory(string rule, GameIdentity game)
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ["root"] = Path.GetPathRoot(game.InstallDirectory) ?? string.Empty,
            ["game"] = game.InstallDirectory, ["base"] = game.InstallDirectory, ["storeGameId"] = game.ProviderGameId ?? string.Empty,
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

    private static string? Scalar(YamlMappingNode node, string parent, string child) => node.Children.TryGetValue(new YamlScalarNode(parent), out YamlNode? value) && value is YamlMappingNode map && map.Children.TryGetValue(new YamlScalarNode(child), out YamlNode? scalar) ? (scalar as YamlScalarNode)?.Value : null;
    private static IEnumerable<string> Sequence(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlSequenceNode sequence ? sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value!).Where(item => !string.IsNullOrWhiteSpace(item)) : [];
    private static IEnumerable<string> MappingKeys(YamlMappingNode node, string key) => node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlMappingNode mapping ? mapping.Children.Keys.OfType<YamlScalarNode>().Select(item => item.Value!).Where(item => !string.IsNullOrWhiteSpace(item)) : [];

    private sealed record ManifestEntry(string Name, string? SteamId, string? GogId, IReadOnlyList<string> Aliases, IReadOnlyList<string> Files)
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
        public ManifestEntry? Find(GameIdentity game) => entries.FirstOrDefault(entry => entry.Matches(game));
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), "[\\s:_'?\\-]", string.Empty)
        .Replace("gameoftheyear", string.Empty).Replace("goty", string.Empty).Replace("definitiveedition", string.Empty).Replace("remastered", string.Empty).Replace("deluxeedition", string.Empty);
}
