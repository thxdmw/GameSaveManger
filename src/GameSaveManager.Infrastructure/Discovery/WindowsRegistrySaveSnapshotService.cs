using System.Runtime.Versioning;
using System.Text.Json;
using GameSaveManager.Application.Games;
using Microsoft.Win32;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>仅处理明确配置的 HKCU 键，将其转换为可随普通快照同步的 JSON 文件。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistrySaveSnapshotService : IRegistrySaveSnapshotService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task ExportAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken)
    {
        if (rules.Count == 0) return;
        Directory.CreateDirectory(directory);
        foreach (RegistrySaveRule rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string subKey = GetCurrentUserSubKey(rule);
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            RegistryNode node = key is null ? new RegistryNode([], []) : ReadNode(key, cancellationToken);
            string temporary = GetDocumentPath(directory, rule) + ".tmp";
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(new RegistryDocument(rule.KeyPath, node), JsonOptions), cancellationToken);
            File.Move(temporary, GetDocumentPath(directory, rule), overwrite: true);
        }
    }

    public async Task ImportAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken)
    {
        if (rules.Count == 0) return;
        string safetyDirectory = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(directory))!, $"registry-safety-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}");
        await ExportAsync(safetyDirectory, rules, cancellationToken);
        foreach (RegistrySaveRule rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string documentPath = GetDocumentPath(directory, rule);
            if (!File.Exists(documentPath)) throw new InvalidDataException($"缺少注册表存档文件: {rule.RuleId}");
            RegistryDocument? document = JsonSerializer.Deserialize<RegistryDocument>(await File.ReadAllTextAsync(documentPath, cancellationToken));
            if (document is null || !string.Equals(document.KeyPath, rule.KeyPath, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"注册表存档文件与配置规则不匹配: {rule.RuleId}");
            string subKey = GetCurrentUserSubKey(rule);
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            using RegistryKey target = Registry.CurrentUser.CreateSubKey(subKey, writable: true)
                ?? throw new IOException($"无法创建注册表键: {rule.KeyPath}");
            WriteNode(target, document.Node, cancellationToken);
        }
    }

    private static RegistryNode ReadNode(RegistryKey key, CancellationToken cancellationToken) =>
        new(key.GetValueNames().Select(name =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            object value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames) ?? string.Empty;
            return new RegistryValueEntry(name, key.GetValueKind(name).ToString(), JsonSerializer.SerializeToElement(value));
        }).ToArray(), key.GetSubKeyNames().Select(name =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            using RegistryKey child = key.OpenSubKey(name, writable: false)!;
            return new RegistryChild(name, ReadNode(child, cancellationToken));
        }).ToArray());

    private static void WriteNode(RegistryKey key, RegistryNode node, CancellationToken cancellationToken)
    {
        foreach (RegistryValueEntry value in node.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            key.SetValue(value.Name, DeserializeValue(value), Enum.Parse<RegistryValueKind>(value.Kind, ignoreCase: true));
        }
        foreach (RegistryChild child in node.Children)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using RegistryKey childKey = key.CreateSubKey(child.Name, writable: true) ?? throw new IOException($"无法创建注册表子键: {child.Name}");
            WriteNode(childKey, child.Node, cancellationToken);
        }
    }

    private static object DeserializeValue(RegistryValueEntry entry) => Enum.Parse<RegistryValueKind>(entry.Kind, true) switch
    {
        RegistryValueKind.Binary => entry.Value.Deserialize<byte[]>() ?? [],
        RegistryValueKind.DWord => entry.Value.GetInt32(),
        RegistryValueKind.QWord => entry.Value.GetInt64(),
        RegistryValueKind.MultiString => entry.Value.Deserialize<string[]>() ?? [],
        _ => entry.Value.GetString() ?? string.Empty
    };

    private static string GetCurrentUserSubKey(RegistrySaveRule rule)
    {
        if (!rule.UserConfirmed || string.IsNullOrWhiteSpace(rule.RuleId) || rule.RuleId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            throw new InvalidOperationException("注册表规则必须具有已确认的安全标识。");
        const string longPrefix = "HKEY_CURRENT_USER\\";
        const string shortPrefix = "HKCU\\";
        string path = rule.KeyPath.Trim();
        if (path.StartsWith(longPrefix, StringComparison.OrdinalIgnoreCase)) return path[longPrefix.Length..];
        if (path.StartsWith(shortPrefix, StringComparison.OrdinalIgnoreCase)) return path[shortPrefix.Length..];
        throw new InvalidOperationException("注册表存档仅支持 HKEY_CURRENT_USER（HKCU）路径。");
    }

    private static string GetDocumentPath(string directory, RegistrySaveRule rule) => Path.Combine(directory, rule.RuleId + ".json");

    private sealed record RegistryDocument(string KeyPath, RegistryNode Node);
    private sealed record RegistryNode(IReadOnlyList<RegistryValueEntry> Values, IReadOnlyList<RegistryChild> Children);
    private sealed record RegistryChild(string Name, RegistryNode Node);
    private sealed record RegistryValueEntry(string Name, string Kind, JsonElement Value);
}
