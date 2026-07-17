using System.Runtime.Versioning;
using System.Diagnostics;
using System.Text.Json;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Restores;
using Microsoft.Win32;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>仅处理明确配置的 HKCU 键，将其转换为可随普通快照同步的 JSON 文件。</summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsRegistrySaveSnapshotService : IRegistrySaveSnapshotService, IRegistryRestoreTransaction
{
    private const int MaximumDepth = 32;
    private const int MaximumValues = 10_000;
    private const int MaximumSubKeys = 10_000;
    private const long MaximumEstimatedBytes = 64L * 1024 * 1024;
    private static readonly TimeSpan MaximumDuration = TimeSpan.FromSeconds(30);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, MaxDepth = 128 };

    public async Task<IReadOnlyList<RegistrySavePreview>> PreviewAsync(
        IReadOnlyList<RegistrySaveRule> rules,
        CancellationToken cancellationToken)
    {
        return await Task.Run<IReadOnlyList<RegistrySavePreview>>(
            () => rules.Select(rule => PreviewRule(rule, cancellationToken)).ToArray(),
            cancellationToken);
    }

    public async Task ExportAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken)
    {
        if (rules.Count == 0) return;
        Directory.CreateDirectory(directory);
        foreach (RegistrySaveRule rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string subKey = GetCurrentUserSubKey(rule, requireConfirmed: true);
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            if (key is null) throw new InvalidOperationException($"注册表键不存在：{rule.KeyPath}");
            RegistryPreviewMetrics metrics = InspectNode(key, cancellationToken);
            if (metrics.UnsupportedKinds.Count > 0)
                throw new InvalidDataException(
                    $"注册表键包含不支持的数据类型：{string.Join("、", metrics.UnsupportedKinds)}");
            RegistryNode node = ReadNode(key, cancellationToken);
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
        await ImportDocumentsAsync(directory, rules, cancellationToken);
    }

    public async Task<RegistryRestorePreparation> PrepareAsync(
        string snapshotDirectory,
        IReadOnlyList<RegistrySaveRule> rules,
        string transactionDirectory,
        CancellationToken cancellationToken)
    {
        if (rules.Count == 0)
            return new RegistryRestorePreparation(string.Empty, snapshotDirectory, [], RegistryRestoreState.NotRequired);
        if (rules.Any(rule => !rule.UserConfirmed)
            || rules.Select(rule => rule.RuleId).Distinct(StringComparer.OrdinalIgnoreCase).Count() != rules.Count)
            throw new InvalidOperationException("注册表恢复规则必须已确认且 RuleId 唯一。");

        foreach (RegistrySaveRule rule in rules)
        {
            _ = GetCurrentUserSubKey(rule, requireConfirmed: true);
            await ReadAndValidateDocumentAsync(snapshotDirectory, rule, cancellationToken);
        }

        string safetyDirectory = Path.Combine(transactionDirectory, "registry-safety");
        await ExportAsync(safetyDirectory, rules, cancellationToken);
        return new RegistryRestorePreparation(safetyDirectory, snapshotDirectory, rules.ToArray(), RegistryRestoreState.Prepared);
    }

    public async Task ApplyAsync(RegistryRestorePreparation preparation, CancellationToken cancellationToken)
    {
        if (preparation.State != RegistryRestoreState.Prepared)
            throw new InvalidOperationException("注册表恢复尚未完成准备阶段。");
        await ImportDocumentsAsync(preparation.SnapshotDirectory, preparation.Rules, cancellationToken);
    }

    public Task RollbackAsync(RegistryRestorePreparation preparation, CancellationToken cancellationToken)
    {
        if (preparation.State == RegistryRestoreState.NotRequired) return Task.CompletedTask;
        return ImportDocumentsAsync(preparation.SafetyDirectory, preparation.Rules, cancellationToken);
    }

    private async Task ImportDocumentsAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken)
    {
        var validated = new List<(RegistrySaveRule Rule, RegistryDocument Document)>(rules.Count);
        foreach (RegistrySaveRule rule in rules)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RegistryDocument document = await ReadAndValidateDocumentAsync(directory, rule, cancellationToken);
            validated.Add((rule, document));
        }

        // 所有文档必须先完整通过预算和类型校验，之后才允许删除任何现有键。
        foreach ((RegistrySaveRule rule, RegistryDocument document) in validated)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string subKey = GetCurrentUserSubKey(rule, requireConfirmed: true);
            Registry.CurrentUser.DeleteSubKeyTree(subKey, throwOnMissingSubKey: false);
            using RegistryKey target = Registry.CurrentUser.CreateSubKey(subKey, writable: true)
                ?? throw new IOException($"无法创建注册表键: {rule.KeyPath}");
            WriteNode(target, document.Node, cancellationToken);
        }
    }

    private static async Task<RegistryDocument> ReadAndValidateDocumentAsync(string directory, RegistrySaveRule rule, CancellationToken cancellationToken)
    {
        string documentPath = GetDocumentPath(directory, rule);
        if (!File.Exists(documentPath)) throw new InvalidDataException($"缺少注册表存档文件: {rule.RuleId}");
        if (new FileInfo(documentPath).Length > MaximumEstimatedBytes)
            throw new InvalidDataException("注册表存档文件超过 64 MB，已拒绝恢复。");
        await using FileStream stream = new(documentPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 64 * 1024, useAsync: true);
        RegistryDocument? document;
        try
        {
            document = await JsonSerializer.DeserializeAsync<RegistryDocument>(stream, JsonOptions, cancellationToken);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"注册表存档 JSON 无效: {rule.RuleId}", exception);
        }
        if (document is null || !string.Equals(document.KeyPath, rule.KeyPath, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"注册表存档文件与配置规则不匹配: {rule.RuleId}");
        RegistryDocumentValidator.Validate(document.Node, cancellationToken);
        return document;
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

    private static RegistrySavePreview PreviewRule(RegistrySaveRule rule, CancellationToken cancellationToken)
    {
        try
        {
            string subKey = GetCurrentUserSubKey(rule, requireConfirmed: false);
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(subKey, writable: false);
            if (key is null) return new RegistrySavePreview(rule, false, false, 0, 0, [], null);
            RegistryPreviewMetrics metrics = InspectNode(key, cancellationToken);
            return new RegistrySavePreview(rule, true, true, metrics.ValueCount, metrics.EstimatedSize,
                metrics.UnsupportedKinds.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToArray(), null);
        }
        catch (Exception exception) when (exception is UnauthorizedAccessException
            or System.Security.SecurityException or IOException or InvalidOperationException
            or ArgumentException or OverflowException or TimeoutException)
        {
            return new RegistrySavePreview(rule, true, false, 0, 0, [], $"注册表预览失败：{exception.Message}");
        }
    }

    private static RegistryPreviewMetrics InspectNode(RegistryKey key, CancellationToken cancellationToken)
    {
        var inspection = new RegistryPreviewInspection();
        inspection.Visit(key, depth: 0, cancellationToken);
        return inspection.ToMetrics();
    }

    private static long EstimateValueSize(string name, object? value) => name.Length * 2L + (value switch
    {
        null => 0,
        byte[] bytes => bytes.LongLength,
        string text => text.Length * 2L,
        string[] values => values.Sum(item => item.Length * 2L),
        int => sizeof(int),
        long => sizeof(long),
        _ => value.ToString()?.Length * 2L ?? 0
    });

    private static string GetCurrentUserSubKey(RegistrySaveRule rule, bool requireConfirmed)
    {
        if ((requireConfirmed && !rule.UserConfirmed) || string.IsNullOrWhiteSpace(rule.RuleId) || rule.RuleId.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
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
    private sealed record RegistryPreviewMetrics(int ValueCount, long EstimatedSize, HashSet<string> UnsupportedKinds);

    private sealed class RegistryPreviewInspection
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private readonly HashSet<string> _unsupportedKinds = new(StringComparer.OrdinalIgnoreCase);
        private int _valueCount;
        private int _subKeyCount;
        private long _estimatedSize;

        public void Visit(RegistryKey key, int depth, CancellationToken cancellationToken)
        {
            CheckBudget(depth, cancellationToken);
            foreach (string name in key.GetValueNames())
            {
                CheckBudget(depth, cancellationToken);
                if (++_valueCount > MaximumValues)
                    throw new InvalidOperationException($"注册表值超过 {MaximumValues} 个，禁止作为存档规则。");
                RegistryValueKind kind = key.GetValueKind(name);
                object? value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                _estimatedSize = checked(_estimatedSize + EstimateValueSize(name, value));
                if (_estimatedSize > MaximumEstimatedBytes)
                    throw new InvalidOperationException("注册表预计导出大小超过 64 MB，禁止作为存档规则。");
                if (kind is not (RegistryValueKind.String or RegistryValueKind.ExpandString
                    or RegistryValueKind.Binary or RegistryValueKind.DWord or RegistryValueKind.QWord
                    or RegistryValueKind.MultiString))
                    _unsupportedKinds.Add(kind.ToString());
            }
            foreach (string childName in key.GetSubKeyNames())
            {
                CheckBudget(depth, cancellationToken);
                if (++_subKeyCount > MaximumSubKeys)
                    throw new InvalidOperationException($"注册表子键超过 {MaximumSubKeys} 个，禁止作为存档规则。");
                _estimatedSize = checked(_estimatedSize + childName.Length * 2L);
                using RegistryKey? child = key.OpenSubKey(childName, writable: false);
                if (child is null) throw new UnauthorizedAccessException($"无法读取注册表子键：{childName}");
                Visit(child, depth + 1, cancellationToken);
            }
        }

        public RegistryPreviewMetrics ToMetrics() =>
            new(_valueCount, _estimatedSize, _unsupportedKinds);

        private void CheckBudget(int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > MaximumDepth)
                throw new InvalidOperationException($"注册表层级超过 {MaximumDepth} 层，禁止作为存档规则。");
            if (_stopwatch.Elapsed > MaximumDuration)
                throw new TimeoutException("注册表预览超过 30 秒，已停止扫描。");
        }
    }

    private sealed class RegistryDocumentValidator
    {
        private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
        private int _valueCount;
        private int _subKeyCount;
        private long _estimatedSize;

        public static void Validate(RegistryNode node, CancellationToken cancellationToken) =>
            new RegistryDocumentValidator().Visit(node, depth: 0, cancellationToken);

        private void Visit(RegistryNode node, int depth, CancellationToken cancellationToken)
        {
            CheckBudget(depth, cancellationToken);
            if (node.Values is null || node.Children is null)
                throw new InvalidDataException("注册表存档节点结构不完整。");
            foreach (RegistryValueEntry value in node.Values)
            {
                CheckBudget(depth, cancellationToken);
                if (value is null || value.Name is null || string.IsNullOrWhiteSpace(value.Kind))
                    throw new InvalidDataException("注册表存档包含不完整的值记录。");
                if (++_valueCount > MaximumValues)
                    throw new InvalidDataException($"注册表存档值超过 {MaximumValues} 个，已拒绝恢复。");
                RegistryValueKind kind;
                try { kind = Enum.Parse<RegistryValueKind>(value.Kind, ignoreCase: true); }
                catch (Exception exception) when (exception is ArgumentException or OverflowException)
                {
                    throw new InvalidDataException($"注册表存档包含未知值类型：{value.Kind}", exception);
                }
                ValidateValueKind(value, kind);
                _estimatedSize = checked(_estimatedSize + EstimateDocumentValueSize(value, kind));
                if (_estimatedSize > MaximumEstimatedBytes)
                    throw new InvalidDataException("注册表存档预计内容超过 64 MB，已拒绝恢复。");
            }
            foreach (RegistryChild child in node.Children)
            {
                CheckBudget(depth, cancellationToken);
                if (string.IsNullOrWhiteSpace(child.Name) || child.Name.Contains('\\'))
                    throw new InvalidDataException("注册表存档包含无效子键名称。");
                if (child.Node is null)
                    throw new InvalidDataException("注册表存档包含空子键节点。");
                if (++_subKeyCount > MaximumSubKeys)
                    throw new InvalidDataException($"注册表存档子键超过 {MaximumSubKeys} 个，已拒绝恢复。");
                _estimatedSize = checked(_estimatedSize + child.Name.Length * 2L);
                Visit(child.Node, depth + 1, cancellationToken);
            }
        }

        private static void ValidateValueKind(RegistryValueEntry entry, RegistryValueKind kind)
        {
            bool valid = kind switch
            {
                RegistryValueKind.String or RegistryValueKind.ExpandString => entry.Value.ValueKind == JsonValueKind.String,
                RegistryValueKind.Binary => CanDeserialize<byte[]>(entry.Value),
                RegistryValueKind.DWord => entry.Value.TryGetInt32(out _),
                RegistryValueKind.QWord => entry.Value.TryGetInt64(out _),
                RegistryValueKind.MultiString => IsValidMultiString(entry.Value),
                _ => false
            };
            if (!valid) throw new InvalidDataException($"注册表值 {entry.Name} 的类型或 JSON 内容无效。");
        }

        private static bool CanDeserialize<T>(JsonElement value)
        {
            try { return value.Deserialize<T>() is not null; }
            catch (JsonException) { return false; }
        }

        private static bool IsValidMultiString(JsonElement value)
        {
            try
            {
                string[]? values = value.Deserialize<string[]>();
                return values is not null && values.All(item => item is not null);
            }
            catch (JsonException) { return false; }
        }

        private static long EstimateDocumentValueSize(RegistryValueEntry entry, RegistryValueKind kind) =>
            entry.Name.Length * 2L + (kind switch
            {
                RegistryValueKind.Binary => entry.Value.Deserialize<byte[]>()?.LongLength ?? 0,
                RegistryValueKind.DWord => sizeof(int),
                RegistryValueKind.QWord => sizeof(long),
                RegistryValueKind.MultiString => entry.Value.Deserialize<string[]>()?.Sum(item => item.Length * 2L) ?? 0,
                _ => (entry.Value.GetString()?.Length ?? 0) * 2L
            });

        private void CheckBudget(int depth, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (depth > MaximumDepth)
                throw new InvalidDataException($"注册表存档层级超过 {MaximumDepth} 层，已拒绝恢复。");
            if (_stopwatch.Elapsed > MaximumDuration)
                throw new TimeoutException("注册表存档验证超过 30 秒，已停止恢复。");
        }
    }
}
