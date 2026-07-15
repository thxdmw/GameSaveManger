namespace GameSaveManager.Application.Discovery;

public sealed record FileMetadataSnapshot(string FullPath, long Size, DateTime LastWriteTimeUtc);
