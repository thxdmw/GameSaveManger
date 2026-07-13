namespace GameSaveManager.Application.Sync;

/// <summary>本机最后一次成功同步后确认的云端 HEAD；ServerKey 隔离不同 GameSave 服务端。</summary>
public sealed record LocalSyncState(
    string ServerKey,
    string GameId,
    string? HeadSnapshotId,
    long HeadVersion);
