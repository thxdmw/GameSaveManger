namespace GameSaveManager.Application.Files;

/// <summary>完整存档目录扫描契约；实现必须返回可用于构建事实 Manifest 的文件元数据。</summary>
public interface ISaveDirectoryScanner
{
    Task<IReadOnlyList<ScannedSaveFile>> ScanAsync(
        string saveDirectory,
        CancellationToken cancellationToken);
}
