namespace GameSaveManager.Application.Files;

/// <summary>文件 SHA-256 计算契约。</summary>
public interface IFileHashService
{
    Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken);
}
