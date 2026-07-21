namespace GameSaveManager.Application.Sync;

public sealed record CloudFreshnessResult(
    CloudFreshnessStatus Status,
    string? RemoteHeadSnapshotId,
    string? LocalBaseSnapshotId);

public enum CloudFreshnessStatus
{
    UpToDate,
    RemoteAheadLocalUnchanged,
    Diverged,
    BaselineMissing
}
