namespace GameSaveManager.Application.Sync;

/// <summary>本机最后一次成功同步后确认的云端 HEAD；ServerKey 隔离不同 GameSave 服务端。</summary>
public sealed record LocalSyncState(
    string ServerKey,
    string GameId,
    string? HeadSnapshotId,
    long HeadVersion,
    string UserId = "")
{
    /// <summary>用户主动恢复了非当前 HEAD；在明确选择版本前禁止自动拉取覆盖。</summary>
    public const long IntentionalRestorePendingVersion = -1;
}
