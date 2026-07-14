using System.Text.Json;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using GameSaveManager.Application.Discovery;
using Microsoft.Win32;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>
/// Windows 平台游戏发现：Steam 读取 libraryfolders/appmanifest，Epic 读取 .item 清单，
/// GOG 读取其安装注册表。发现结果只用于预填信息，绝不猜测游戏存档目录。
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsGameDiscoveryService : IGameDiscoveryService
{
    private static readonly Regex VdfValue = new("\\\"(?<key>[^\\\"]+)\\\"\\s+\\\"(?<value>[^\\\"]*)\\\"", RegexOptions.Compiled);

    public Task<IReadOnlyList<DiscoveredGame>> DiscoverAsync(CancellationToken cancellationToken)
    {
        return Task.Run(() =>
        {
            var games = new List<DiscoveredGame>();
            DiscoverSteam(games, cancellationToken);
            DiscoverEpic(games, cancellationToken);
            DiscoverGog(games, cancellationToken);
            return (IReadOnlyList<DiscoveredGame>)games
                .GroupBy(game => NormalizeInstallDirectory(game), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }, cancellationToken);
    }

    private static string NormalizeInstallDirectory(DiscoveredGame game)
    {
        if (string.IsNullOrWhiteSpace(game.InstallDirectory))
        {
            return $"{game.Provider}:{game.ProviderGameId}";
        }
        return Path.GetFullPath(game.InstallDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
    private static void DiscoverSteam(List<DiscoveredGame> games, CancellationToken cancellationToken)
    {
        var libraries = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? steamPath = Registry.CurrentUser.OpenSubKey("Software\\Valve\\Steam")?.GetValue("SteamPath") as string;
        if (!string.IsNullOrWhiteSpace(steamPath)) libraries.Add(steamPath);
        string defaultSteam = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
        if (Directory.Exists(defaultSteam)) libraries.Add(defaultSteam);

        foreach (string root in libraries.ToArray())
        {
            cancellationToken.ThrowIfCancellationRequested();
            string vdfPath = Path.Combine(root, "steamapps", "libraryfolders.vdf");
            if (!File.Exists(vdfPath)) continue;
            foreach (Match match in VdfValue.Matches(File.ReadAllText(vdfPath)))
            {
                if (string.Equals(match.Groups["key"].Value, "path", StringComparison.OrdinalIgnoreCase))
                {
                    libraries.Add(match.Groups["value"].Value.Replace("\\\\", "\\"));
                }
            }
        }

        foreach (string library in libraries)
        {
            string steamApps = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamApps)) continue;
            foreach (string manifest in Directory.EnumerateFiles(steamApps, "appmanifest_*.acf"))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Dictionary<string, string> values = ParseVdf(File.ReadAllText(manifest));
                if (!values.TryGetValue("appid", out string? appId)
                    || !values.TryGetValue("name", out string? name)
                    || !values.TryGetValue("installdir", out string? installName)) continue;
                string installDirectory = Path.Combine(steamApps, "common", installName);
                if (!Directory.Exists(installDirectory)) continue;
                string? executable = FindExecutable(installDirectory);
                games.Add(new DiscoveredGame(name, "STEAM", appId, installDirectory, executable, executable is null ? null : Path.GetFileName(executable)));
            }
        }
    }

    private static void DiscoverEpic(List<DiscoveredGame> games, CancellationToken cancellationToken)
    {
        string manifests = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "Epic", "EpicGamesLauncher", "Data", "Manifests");
        if (!Directory.Exists(manifests)) return;
        foreach (string item in Directory.EnumerateFiles(manifests, "*.item"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(item));
                JsonElement root = document.RootElement;
                string? name = ReadString(root, "DisplayName");
                string? id = ReadString(root, "CatalogItemId") ?? ReadString(root, "AppName");
                string? install = ReadString(root, "InstallLocation");
                string? launch = ReadString(root, "LaunchExecutable");
                if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(install) || !Directory.Exists(install)) continue;
                string? executable = string.IsNullOrWhiteSpace(launch) ? FindExecutable(install) : Path.Combine(install, launch);
                if (!File.Exists(executable)) executable = FindExecutable(install);
                games.Add(new DiscoveredGame(name, "EPIC", id, install, executable, executable is null ? null : Path.GetFileName(executable)));
            }
            catch (JsonException)
            {
                // 个别损坏清单不应阻断其他游戏平台发现。
            }
        }
    }

    private static void DiscoverGog(List<DiscoveredGame> games, CancellationToken cancellationToken)
    {
        foreach (RegistryKey baseKey in OpenGogKeys())
        using (baseKey)
        {
            foreach (string subKeyName in baseKey.GetSubKeyNames())
            {
                cancellationToken.ThrowIfCancellationRequested();
                using RegistryKey? gameKey = baseKey.OpenSubKey(subKeyName);
                string? install = gameKey?.GetValue("path") as string;
                if (string.IsNullOrWhiteSpace(install) || !Directory.Exists(install)) continue;
                string name = gameKey?.GetValue("gameName") as string ?? $"GOG {subKeyName}";
                string? executable = FindExecutable(install);
                games.Add(new DiscoveredGame(name, "GOG", subKeyName, install, executable, executable is null ? null : Path.GetFileName(executable)));
            }
        }
    }

    private static IEnumerable<RegistryKey> OpenGogKeys()
    {
        RegistryKey? localMachine = Registry.LocalMachine.OpenSubKey("SOFTWARE\\WOW6432Node\\GOG.com\\Games");
        if (localMachine is not null) yield return localMachine;
        RegistryKey? currentUser = Registry.CurrentUser.OpenSubKey("SOFTWARE\\GOG.com\\Games");
        if (currentUser is not null) yield return currentUser;
    }

    private static Dictionary<string, string> ParseVdf(string content) => VdfValue.Matches(content)
        .Cast<Match>()
        .GroupBy(match => match.Groups["key"].Value, StringComparer.OrdinalIgnoreCase)
        .ToDictionary(group => group.Key, group => group.Last().Groups["value"].Value, StringComparer.OrdinalIgnoreCase);

    private static string? ReadString(JsonElement root, string propertyName) =>
        root.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? FindExecutable(string directory)
    {
        try
        {
            return Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly)
                .OrderBy(path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}