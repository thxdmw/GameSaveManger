namespace GameSaveManager.Domain.Snapshots;

/// <summary>客户端领域中的不可变存档快照；新版本通过 ParentSnapshotId 形成时间线。</summary>
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
    /// <summary>Manifest 中所有文件大小之和，不代表实际去重后的物理占用。</summary>
    public long LogicalSize => Files.Sum(file => file.Size);
}
