namespace GameSaveManager.Application.Sync;

public sealed record CloudFreshnessResult(
    CloudFreshnessStatus Status,
    string? RemoteHeadSnapshotId,
    string? LocalBaseSnapshotId,
    int LocalFileCount = 0,
    long LocalLogicalSize = 0);

public enum CloudFreshnessStatus
{
    UpToDate,
    LocalAhead,
    LocalDataMissing,
    RemoteAheadLocalUnchanged,
    Diverged,
    BaselineMissing
}
