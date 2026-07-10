namespace GameSaveManager.Domain.Snapshots;

public sealed record SnapshotFile(
    string RelativePath,
    string Sha256,
    long Size);
