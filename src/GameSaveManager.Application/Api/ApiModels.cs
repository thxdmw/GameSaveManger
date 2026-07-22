using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Application.Api;

/// <summary>注册或登录成功后的设备会话；DeviceToken 应立即交给系统凭据存储。</summary>
public sealed record AuthSession(string UserId, string DeviceId, string DeviceToken);

/// <summary>当前设备 Token 对应的稳定账号与设备身份。</summary>
public sealed record CloudAccountSession(string UserId, string DeviceId, string Username);

/// <summary>云端逻辑游戏，不包含任何本机绝对路径。</summary>
public sealed record CloudGame(string GameId, string Name, string Provider, string? ProviderGameId);
/// <summary>当前账号已登记设备的安全摘要，不含 Token。</summary>
/// <summary>当前账号按去重内容对象计算的物理存储配额摘要。</summary>
/// <summary>单个云端游戏的快照自动保留策略。</summary>
public sealed record CloudRetentionPolicy(
    string GameId,
    bool Enabled,
    int RetentionCount,
    int RetentionDays);

/// <summary>手动或自动执行保留策略后的清理摘要。</summary>
public sealed record CloudRetentionCleanupResult(string GameId, int DeletedSnapshotCount);
public sealed record CloudQuota(long QuotaBytes, long UsedBytes, long RemainingBytes);
public sealed record CloudDevice(
    string DeviceId,
    string DeviceName,
    DateTimeOffset? LastSeenTime,
    bool Active,
    DateTimeOffset? CreateTime)
{
    public DateTimeOffset? LocalLastSeenTime => LastSeenTime?.ToLocalTime();
    public DateTimeOffset? LocalCreateTime => CreateTime?.ToLocalTime();
}

/// <summary>指定游戏当前云端同步 HEAD。</summary>
public sealed record CloudHead(string GameId, string? HeadSnapshotId, long Version);

/// <summary>以 SHA-256 和大小唯一描述一个用户级内容对象。</summary>
public sealed record ContentObjectDescriptor(string Sha256, long Size);

/// <summary>云端快照提交结果；Created=false 表示 Manifest 未变化，本次为幂等 no-op。</summary>
public sealed record CloudSnapshotResult(
    string SnapshotId,
    long HeadVersion,
    int FileCount,
    long LogicalSize,
    int ChangedFileCount,
    bool Created);
/// <summary>时间线展示用的轻量快照信息，不包含完整文件清单。</summary>
public sealed record CloudSnapshotSummary(
    string SnapshotId,
    string? ParentSnapshotId,
    string DeviceId,
    string TriggerType,
    string? Description,
    int FileCount,
    long LogicalSize,
    int ChangedFileCount,
    DateTimeOffset CreateTime,
    IReadOnlyList<CloudSnapshotRoot>? Roots = null)
{
    public DateTimeOffset LocalCreateTime => CreateTime.ToLocalTime();
    public string TriggerDisplayText => SnapshotTriggerNames.ToDisplayName(TriggerType);
    public string RootSummaryText => Roots is not { Count: > 0 }
        ? "未记录存档路径"
        : string.Join("；", Roots.Select(root => $"{root.RootId}: {root.PathTemplate ?? root.RootType}"));
}

/// <summary>统一的快照触发类型字符串转换。</summary>
public static class SnapshotTriggerNames
{
    public static string ToDisplayName(string triggerType) => triggerType switch
    {
        "MANUAL" => "手动同步",
        "GAME_EXIT" => "自动同步（游戏退出）",
        "BEFORE_RESTORE" => "恢复前安全快照",
        "IMPORT" => "导入快照",
        _ => triggerType
    };

    public static string ToApiValue(SnapshotTrigger trigger) => trigger switch
    {
        SnapshotTrigger.Manual => "MANUAL",
        SnapshotTrigger.GameExit => "GAME_EXIT",
        SnapshotTrigger.BeforeRestore => "BEFORE_RESTORE",
        SnapshotTrigger.Import => "IMPORT",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "未知快照触发类型")
    };
}
/// <summary>云端不可变快照的完整清单，包含根目录元数据与内容对象引用。</summary>
public sealed record CloudSnapshotManifest(
    string SnapshotId,
    string GameId,
    string DeviceId,
    string? ParentSnapshotId,
    string TriggerType,
    string? Description,
    DateTimeOffset CreateTime,
    IReadOnlyList<CloudSnapshotRoot>? Roots,
    IReadOnlyList<CloudSnapshotFile> Files);

/// <summary>快照创建时使用的可移植存档根目录描述。</summary>
public sealed record CloudSnapshotRoot(
    string RootId,
    string RootType,
    string? PathTemplate,
    string Source,
    int Confidence,
    IReadOnlyList<string>? IncludePatterns,
    IReadOnlyList<string>? ExcludePatterns);

/// <summary>客户端提交的快照根目录元数据。</summary>
public sealed record SnapshotRootDescriptor(
    string RootId,
    string RootType,
    string? PathTemplate,
    string Source,
    int Confidence,
    IReadOnlyList<string> IncludePatterns,
    IReadOnlyList<string> ExcludePatterns);

/// <summary>快照中的单个文件；ObjectId 是服务端授权下载所需的内容对象标识。</summary>
public sealed record CloudSnapshotFile(
    string RelativePath,
    string ObjectId,
    string Sha256,
    long Size);
