using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Application.Api;

/// <summary>注册或登录成功后的设备会话；DeviceToken 应立即交给系统凭据存储。</summary>
public sealed record AuthSession(string UserId, string DeviceId, string DeviceToken);

/// <summary>云端逻辑游戏，不包含任何本机绝对路径。</summary>
public sealed record CloudGame(string GameId, string Name, string Provider, string? ProviderGameId);

/// <summary>指定游戏当前云端同步 HEAD。</summary>
public sealed record CloudHead(string GameId, string? HeadSnapshotId, long Version);

/// <summary>以 SHA-256 和大小唯一描述一个用户级内容对象。</summary>
public sealed record ContentObjectDescriptor(string Sha256, long Size);

/// <summary>云端快照提交成功结果。</summary>
public sealed record CloudSnapshotResult(
    string SnapshotId,
    long HeadVersion,
    int FileCount,
    long LogicalSize,
    int ChangedFileCount);

/// <summary>统一的快照触发类型字符串转换。</summary>
public static class SnapshotTriggerNames
{
    public static string ToApiValue(SnapshotTrigger trigger) => trigger switch
    {
        SnapshotTrigger.Manual => "MANUAL",
        SnapshotTrigger.GameExit => "GAME_EXIT",
        SnapshotTrigger.BeforeRestore => "BEFORE_RESTORE",
        SnapshotTrigger.Import => "IMPORT",
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "未知快照触发类型")
    };
}
