namespace GameSaveManager.Application.Files;

/// <summary>目录扫描得到的文件元数据；RelativePath 使用统一正斜杠路径。</summary>
public sealed record ScannedSaveFile(
    string RelativePath,
    string FullPath,
    long Size,
    DateTime LastWriteTimeUtc);
