namespace GameSaveManager.Domain.Snapshots;

public sealed record GameSnapshot(
    string SnapshotId,
    string GameId,
    string DeviceId,
    string? ParentSnapshotId,
    SnapshotTrigger Trigger,
    string? Description,
    DateTimeOffset CreatedAt,
    IReadOnlyList<SnapshotFile> Files)
{
    public long LogicalSize => Files.Sum(file => file.Size);
}
