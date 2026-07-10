namespace GameSaveManager.Application.Sync;

/// <summary>本机最后一次成功同步后确认的云端 HEAD。</summary>
public sealed record LocalSyncState(string GameId, string? HeadSnapshotId, long HeadVersion);
