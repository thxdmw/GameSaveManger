namespace GameSaveManager.Application.Games;

/// <summary>把明确配置的注册表键导出为受控 JSON 文件，并在恢复后导入。</summary>
public interface IRegistrySaveSnapshotService
{
    Task<IReadOnlyList<RegistrySavePreview>> PreviewAsync(
        IReadOnlyList<RegistrySaveRule> rules,
        CancellationToken cancellationToken);
    Task ExportAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken);
    Task ImportAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken);
}

public sealed record RegistrySavePreview(
    RegistrySaveRule Rule,
    bool KeyExists,
    bool IsReadable,
    int ValueCount,
    long EstimatedSize,
    IReadOnlyList<string> UnsupportedValueKinds,
    string? Error)
{
    public bool CanConfirm => KeyExists && IsReadable && UnsupportedValueKinds.Count == 0 && string.IsNullOrWhiteSpace(Error);
    public string Summary => Error is { Length: > 0 }
        ? Error
        : !KeyExists
            ? "注册表键不存在。"
            : !IsReadable
                ? "注册表键不可读。"
                : UnsupportedValueKinds.Count > 0
                    ? $"包含不支持的数据类型：{string.Join("、", UnsupportedValueKinds)}"
                    : $"{ValueCount} 个值，预计导出 {FormatBytes(EstimatedSize)}。";

    private static string FormatBytes(long bytes) => bytes < 1024 ? $"{bytes} B" : $"{bytes / 1024d:0.##} KB";
}
