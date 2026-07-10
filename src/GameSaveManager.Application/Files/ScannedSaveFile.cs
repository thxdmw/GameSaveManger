namespace GameSaveManager.Application.Files;

public sealed record ScannedSaveFile(
    string RelativePath,
    string FullPath,
    long Size,
    DateTime LastWriteTimeUtc);
