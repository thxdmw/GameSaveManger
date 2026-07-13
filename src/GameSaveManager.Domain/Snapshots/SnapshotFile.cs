namespace GameSaveManager.Domain.Snapshots;

/// <summary>Snapshot Manifest 中的单个文件；路径统一使用正斜杠，内容身份统一使用 SHA-256。</summary>
public sealed record SnapshotFile(
    string RelativePath,
    string Sha256,
    long Size);
