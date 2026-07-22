namespace GameSaveManager.Application.Sync;

/// <summary>一次主动云同步的结果。</summary>
public sealed record CloudSyncResult(
    CloudSyncStatus Status,
    string Message,
    string? SnapshotId,
    int UploadedObjectCount,
    int FileCount,
    long LogicalSize,
    TimeSpan Duration,
    string? RemoteHeadSnapshotId = null,
    int RemovedFileCount = 0);

public enum CloudSyncStatus
{
    Success,
    RemoteAhead
}
