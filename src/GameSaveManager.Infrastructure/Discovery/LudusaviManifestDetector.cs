using System.Text.RegularExpressions;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Infrastructure.Persistence;
using YamlDotNet.RepresentationModel;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>从 Ludusavi Manifest 按平台、安装目录或名称推导 Windows 存档目录。</summary>
internal static class LudusaviManifestDetector
{
    private const int MaximumGlobDepth = 12;
    private const int MaximumVisitedDirectories = 5_000;
    private const int MaximumGlobResults = 64;
    private static readonly Regex Placeholder = new("<(?<name>[^>]+)>", RegexOptions.Compiled);
    private static Lazy<ManifestIndex> _index = new(LoadIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    public static IReadOnlyList<SaveLocationCandidate> Detect(GameIdentity game, CancellationToken cancellationToken)
    {
        string secondaryPath = Path.Combine(game.InstallDirectory, ".ludusavi.yaml");
        ManifestMatch? match = File.Exists(secondaryPath)
            ? TryLoadIndex(secondaryPath)?.FindSecondary(game)
            : null;
        match ??= _index.Value.Find(game);
        return match is null ? [] : Detect(game, match, cancellationToken);
    }

    internal static IReadOnlyList<SaveLocationCandidate> DetectFromManifestFile(
        GameIdentity game,
        string manifestPath,
        CancellationToken cancellationToken = default)
    {
        ManifestMatch? match = LoadIndex(manifestPath).Find(game);
        return match is null ? [] : Detect(game, match, cancellationToken);
    }

    private static IReadOnlyList<SaveLocationCandidate> Detect(
        GameIdentity game,
        ManifestMatch match,
        CancellationToken cancellationToken)
    {
        var candidates = new Dictionary<string, SaveLocationCandidate>(StringComparer.OrdinalIgnoreCase);
        foreach (ManifestFileRule rule in match.Entry.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsRuleAllowed(rule, game.Provider)) continue;
            // Ludusavi 明确标为 config 等非存档用途的路径不能成为存档候选；
            // 无 tags 的旧规则继续兼容，config+save 规则也仍然有效。
            if (rule.Tags.Count > 0
                && !rule.Tags.Contains("save", StringComparer.OrdinalIgnoreCase)) continue;
            foreach (string directory in ResolveDirectories(rule.Path, game, match, cancellationToken))
            {
                SaveLocationCandidate? candidate = SaveLocationCandidateFactory.Create(
                    directory,
                    match.Confidence,
                    SaveLocationSource.LudusaviManifest,
                    $"Ludusavi Manifest：{match.Entry.Name}",
                    cancellationToken: cancellationToken);
                if (candidate is not null) candidates.TryAdd(candidate.Path, candidate);
            }
        }
        return candidates.Values.ToArray();
    }

    internal static void ValidateManifestFile(string path)
    {
        ManifestIndex index = LoadIndex(path);
        if (index.Count < 10) throw new InvalidDataException("Ludusavi Manifest 有效游戏条目数量不足。");
    }

    internal static void Invalidate() =>
        _index = new Lazy<ManifestIndex>(LoadIndex, LazyThreadSafetyMode.ExecutionAndPublication);

    private static ManifestIndex LoadIndex()
    {
        string installedPath = Path.Combine(AppContext.BaseDirectory, "Assets", "ludusavi-manifest.yaml");
        string updatedPath = Path.Combine(AppDataPaths.RootDirectory, "manifest", "ludusavi-manifest.yaml");
        string path = File.Exists(updatedPath) ? updatedPath : installedPath;
        return File.Exists(path) ? LoadIndex(path) : new ManifestIndex([]);
    }

    private static ManifestIndex? TryLoadIndex(string path)
    {
        try { return LoadIndex(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidDataException or YamlDotNet.Core.YamlException)
        {
            return null;
        }
    }

    private static ManifestIndex LoadIndex(string path)
    {
        using var reader = new StreamReader(path);
        var stream = new YamlStream();
        stream.Load(reader);
        if (stream.Documents.FirstOrDefault()?.RootNode is not YamlMappingNode root)
            throw new InvalidDataException("Ludusavi Manifest 根节点必须包含游戏映射。");

        var entries = new List<ManifestEntry>();
        if (LooksLikeGameEntry(root))
        {
            entries.Add(ParseEntry(Path.GetFileName(Path.GetDirectoryName(path)) ?? "Local game", root));
        }
        else
        {
            foreach ((YamlNode nameNode, YamlNode dataNode) in root.Children)
            {
                if (nameNode is YamlScalarNode { Value: { Length: > 0 } name } && dataNode is YamlMappingNode data)
                    entries.Add(ParseEntry(name, data));
            }
        }
        return new ManifestIndex(entries.Where(entry => entry.Files.Count > 0 || entry.AliasTarget is not null).ToArray());
    }

    private static bool LooksLikeGameEntry(YamlMappingNode root) =>
        root.Children.ContainsKey(new YamlScalarNode("files")) ||
        root.Children.ContainsKey(new YamlScalarNode("installDir")) ||
        root.Children.ContainsKey(new YamlScalarNode("steam"));

    private static ManifestEntry ParseEntry(string name, YamlMappingNode data)
    {
        string? aliasTarget = Scalar(data, "alias");
        IReadOnlyList<string> aliases = Sequence(data, "alias").Append(name).ToArray();
        IReadOnlyList<string> installDirectories = MappingKeys(data, "installDir").ToArray();
        IReadOnlyList<string> steamIds = Values(Scalar(data, "steam", "id"), Sequence(data, "id", "steamExtra"));
        IReadOnlyList<string> gogIds = Values(Scalar(data, "gog", "id"), Sequence(data, "id", "gogExtra"));
        IReadOnlyList<ManifestFileRule> files = ReadFiles(data);
        return new ManifestEntry(name, steamIds, gogIds, aliases, installDirectories, files, aliasTarget);
    }

    private static IReadOnlyList<string> Values(string? primary, IEnumerable<string> extras) =>
        (string.IsNullOrWhiteSpace(primary) ? extras : extras.Prepend(primary)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

    private static IReadOnlyList<ManifestFileRule> ReadFiles(YamlMappingNode data)
    {
        if (!data.Children.TryGetValue(new YamlScalarNode("files"), out YamlNode? filesNode) || filesNode is not YamlMappingNode files)
            return [];
        var result = new List<ManifestFileRule>();
        foreach ((YamlNode pathNode, YamlNode detailsNode) in files.Children)
        {
            if (pathNode is not YamlScalarNode { Value: { Length: > 0 } path }) continue;
            IReadOnlyList<string> tags = detailsNode is YamlMappingNode details
                ? Sequence(details, "tags").ToArray()
                : [];
            result.Add(new ManifestFileRule(path, ReadConditions(detailsNode), tags));
        }
        return result;
    }

    private static IReadOnlyList<ManifestCondition> ReadConditions(YamlNode node)
    {
        if (node is not YamlMappingNode mapping ||
            !mapping.Children.TryGetValue(new YamlScalarNode("when"), out YamlNode? when)) return [];
        IEnumerable<YamlMappingNode> conditions = when switch
        {
            YamlMappingNode single => [single],
            YamlSequenceNode sequence => sequence.Children.OfType<YamlMappingNode>(),
            _ => []
        };
        return conditions.Select(condition => new ManifestCondition(
            Scalar(condition, "os"), Scalar(condition, "store"))).ToArray();
    }

    private static bool IsRuleAllowed(ManifestFileRule rule, string provider)
    {
        if (rule.Conditions.Count == 0) return true;
        string? store = provider.ToUpperInvariant() switch
        {
            GameIdentity.Steam => "steam",
            GameIdentity.Gog => "gog",
            GameIdentity.Epic => "epic",
            _ => null
        };
        return rule.Conditions.Any(condition =>
            (string.IsNullOrWhiteSpace(condition.Os) || HasValue(condition.Os, "windows")) &&
            (string.IsNullOrWhiteSpace(condition.Store) || store is not null && HasValue(condition.Store, store)));
    }

    private static bool HasValue(string source, string expected) => source
        .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        .Contains(expected, StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> ResolveDirectories(
        string rule,
        GameIdentity game,
        ManifestMatch match,
        CancellationToken cancellationToken)
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string publicDocuments = Environment.GetFolderPath(Environment.SpecialFolder.CommonDocuments);
        string publicDirectory = Directory.GetParent(publicDocuments)?.FullName ?? publicDocuments;
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["home"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ["root"] = FindStoreRoot(game.InstallDirectory) ?? string.Empty,
            ["game"] = ResolveGamePlaceholder(game, match),
            ["base"] = game.InstallDirectory,
            ["storeGameId"] = game.ProviderGameId ?? string.Empty,
            ["storeUserId"] = "*",
            ["osUserName"] = Environment.UserName,
            ["winDocuments"] = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            ["winAppData"] = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ["winLocalAppData"] = localAppData,
            ["winLocalAppDataLow"] = Path.GetFullPath(Path.Combine(localAppData, "..", "LocalLow")),
            ["winPublic"] = publicDirectory,
            ["winProgramData"] = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            ["winDir"] = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };

        Match firstPlaceholder = Placeholder.Match(rule);
        string allowedRoot = firstPlaceholder.Success && values.TryGetValue(firstPlaceholder.Groups["name"].Value, out string? root)
            ? root
            : game.InstallDirectory;
        if (string.IsNullOrWhiteSpace(allowedRoot)) return [];

        string expanded = Placeholder.Replace(rule.Replace('/', Path.DirectorySeparatorChar), match =>
            values.TryGetValue(match.Groups["name"].Value, out string? value) ? value : match.Value);
        if (Placeholder.IsMatch(expanded)) return [];

        try
        {
            allowedRoot = Path.GetFullPath(allowedRoot);
            string fullPattern = Path.GetFullPath(expanded);
            if (!IsPathInside(allowedRoot, fullPattern)) return [];
            string relativePattern = Path.GetRelativePath(allowedRoot, fullPattern);
            if (relativePattern.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Any(segment => segment is ".." or "." or "")) return [];
            return ExpandGlob(allowedRoot, relativePattern, cancellationToken);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException or IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string ResolveGamePlaceholder(GameIdentity game, ManifestMatch match)
    {
        if (!string.IsNullOrWhiteSpace(match.MatchedInstallDirectory))
            return match.MatchedInstallDirectory;
        string actualDirectory = Path.GetFileName(
            game.InstallDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return !string.IsNullOrWhiteSpace(actualDirectory)
            ? actualDirectory
            : Normalize(game.Name);
    }

    private static IReadOnlyList<string> ExpandGlob(string allowedRoot, string relativePattern, CancellationToken cancellationToken)
    {
        string[] segments = relativePattern.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar], StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0 || segments.Length > 32) return [];
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int visitedDirectories = 0;

        void Walk(string current, int index, int depth)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (results.Count >= MaximumGlobResults || visitedDirectories >= MaximumVisitedDirectories || depth > MaximumGlobDepth) return;
            string segment = segments[index];
            if (segment == "**")
            {
                if (index + 1 < segments.Length) Walk(current, index + 1, depth);
                foreach (string child in EnumerateDirectories(current)) Walk(child, index, depth + 1);
                return;
            }

            bool last = index == segments.Length - 1;
            IEnumerable<string> matches = HasWildcard(segment)
                ? EnumerateEntries(current).Where(path => SegmentMatches(Path.GetFileName(path), segment))
                : [Path.Combine(current, segment)];
            foreach (string match in matches)
            {
                if (results.Count >= MaximumGlobResults) break;
                if (last)
                {
                    if (Directory.Exists(match)) results.Add(Path.GetFullPath(match));
                    else if (File.Exists(match) && Path.GetDirectoryName(match) is { } parent) results.Add(Path.GetFullPath(parent));
                }
                else if (Directory.Exists(match) && !IsReparsePoint(match))
                {
                    Walk(match, index + 1, depth + 1);
                }
            }
        }

        IEnumerable<string> EnumerateEntries(string directory)
        {
            if (++visitedDirectories > MaximumVisitedDirectories) return [];
            try { return Directory.EnumerateFileSystemEntries(directory).Take(MaximumVisitedDirectories).ToArray(); }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or DirectoryNotFoundException) { return []; }
        }

        IEnumerable<string> EnumerateDirectories(string directory) =>
            EnumerateEntries(directory).Where(path => Directory.Exists(path) && !IsReparsePoint(path));

        Walk(allowedRoot, 0, 0);
        return results.ToArray();
    }

    private static bool IsReparsePoint(string path)
    {
        try { return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0; }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return true; }
    }

    private static bool HasWildcard(string segment) => segment.IndexOfAny(['*', '?', '[']) >= 0;

    private static bool SegmentMatches(string value, string pattern)
    {
        var expression = new System.Text.StringBuilder("^");
        for (int index = 0; index < pattern.Length; index++)
        {
            char current = pattern[index];
            if (current == '*') expression.Append(".*");
            else if (current == '?') expression.Append('.');
            else if (current == '[' && pattern.IndexOf(']', index + 1) is int end && end > index + 1)
            {
                string content = pattern[(index + 1)..end].Replace("\\", "\\\\").Replace("^", "\\^");
                expression.Append('[').Append(content).Append(']');
                index = end;
            }
            else expression.Append(Regex.Escape(current.ToString()));
        }
        expression.Append('$');
        return Regex.IsMatch(value, expression.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
    }

    private static bool IsPathInside(string root, string path)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar;
        string normalizedPath = Path.GetFullPath(path);
        return normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ||
               string.Equals(Path.TrimEndingDirectorySeparator(normalizedPath), Path.TrimEndingDirectorySeparator(root), StringComparison.OrdinalIgnoreCase);
    }

    private static string? FindStoreRoot(string installDirectory)
    {
        DirectoryInfo? current = new(installDirectory);
        while (current is not null)
        {
            if (string.Equals(current.Name, "steamapps", StringComparison.OrdinalIgnoreCase)) return current.Parent?.FullName;
            current = current.Parent;
        }
        return null;
    }

    private static string? Scalar(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) ? (value as YamlScalarNode)?.Value : null;

    private static string? Scalar(YamlMappingNode node, string parent, string child) =>
        node.Children.TryGetValue(new YamlScalarNode(parent), out YamlNode? value) && value is YamlMappingNode map
            ? Scalar(map, child)
            : null;

    private static IEnumerable<string> Sequence(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlSequenceNode sequence
            ? sequence.Children.OfType<YamlScalarNode>().Select(item => item.Value!).Where(item => !string.IsNullOrWhiteSpace(item))
            : [];

    private static IEnumerable<string> Sequence(YamlMappingNode node, string parent, string child) =>
        node.Children.TryGetValue(new YamlScalarNode(parent), out YamlNode? value) && value is YamlMappingNode map
            ? Sequence(map, child)
            : [];

    private static IEnumerable<string> MappingKeys(YamlMappingNode node, string key) =>
        node.Children.TryGetValue(new YamlScalarNode(key), out YamlNode? value) && value is YamlMappingNode mapping
            ? mapping.Children.Keys.OfType<YamlScalarNode>().Select(item => item.Value!).Where(item => !string.IsNullOrWhiteSpace(item))
            : [];

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), "[\\s:_'?\\-]", string.Empty)
        .Replace("gameoftheyear", string.Empty)
        .Replace("goty", string.Empty)
        .Replace("definitiveedition", string.Empty)
        .Replace("remastered", string.Empty)
        .Replace("deluxeedition", string.Empty);

    private sealed record ManifestFileRule(
        string Path,
        IReadOnlyList<ManifestCondition> Conditions,
        IReadOnlyList<string> Tags);
    private sealed record ManifestCondition(string? Os, string? Store);
    private sealed record ManifestMatch(
        ManifestEntry Entry,
        int Confidence,
        string? MatchedInstallDirectory);
    private sealed record ManifestEntry(
        string Name,
        IReadOnlyList<string> SteamIds,
        IReadOnlyList<string> GogIds,
        IReadOnlyList<string> Aliases,
        IReadOnlyList<string> InstallDirectories,
        IReadOnlyList<ManifestFileRule> Files,
        string? AliasTarget);

    private sealed class ManifestIndex(IReadOnlyList<ManifestEntry> entries)
    {
        public int Count => entries.Count;

        public ManifestMatch? FindSecondary(GameIdentity game)
        {
            if (entries.Count != 1)
                return Find(game);
            ManifestEntry? resolved = ResolveAliases(entries[0]);
            return resolved is null
                ? null
                : new ManifestMatch(resolved, 100, resolved.InstallDirectories.FirstOrDefault());
        }

        public ManifestMatch? Find(GameIdentity game)
        {
            ManifestEntry? entry = entries.FirstOrDefault(item =>
                string.Equals(game.Provider, GameIdentity.Steam, StringComparison.OrdinalIgnoreCase) &&
                item.SteamIds.Contains(game.ProviderGameId ?? string.Empty, StringComparer.OrdinalIgnoreCase));
            int confidence = 100;
            entry ??= entries.FirstOrDefault(item =>
                string.Equals(game.Provider, GameIdentity.Gog, StringComparison.OrdinalIgnoreCase) &&
                item.GogIds.Contains(game.ProviderGameId ?? string.Empty, StringComparer.OrdinalIgnoreCase));
            if (entry is null)
            {
                string installName = Path.GetFileName(Path.TrimEndingDirectorySeparator(game.InstallDirectory));
                entry = entries.FirstOrDefault(item => item.InstallDirectories.Any(name =>
                    string.Equals(Normalize(name), Normalize(installName), StringComparison.Ordinal)));
                confidence = 90;
            }
            if (entry is null)
            {
                entry = entries.FirstOrDefault(item => item.Aliases.Any(alias =>
                    string.Equals(Normalize(alias), Normalize(game.Name), StringComparison.Ordinal)));
                confidence = 85;
            }
            if (entry is null) return null;

            string? installDirectory = entry.InstallDirectories.FirstOrDefault(name =>
                string.Equals(
                    Normalize(name),
                    Normalize(Path.GetFileName(Path.TrimEndingDirectorySeparator(game.InstallDirectory))),
                    StringComparison.Ordinal));
            entry = ResolveAliases(entry);
            return entry is null ? null : new ManifestMatch(entry, confidence, installDirectory);
        }

        private ManifestEntry? ResolveAliases(ManifestEntry entry)
        {
            var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (entry.AliasTarget is { Length: > 0 } target)
            {
                if (!visited.Add(entry.Name)) return null;
                ManifestEntry? targetEntry = entries.FirstOrDefault(item =>
                    string.Equals(item.Name, target, StringComparison.OrdinalIgnoreCase));
                if (targetEntry is null) return null;
                entry = targetEntry;
            }
            return entry;
        }
    }
}
