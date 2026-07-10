using System.Security.Cryptography;
using GameSaveManager.Application.Files;

namespace GameSaveManager.Infrastructure.FileSystem;

public sealed class FileHashService : IFileHashService
{
    private const int BufferSize = 1024 * 1024;

    public async Task<string> ComputeSha256Async(
        string filePath,
        CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            BufferSize,
            FileOptions.Asynchronous | FileOptions.SequentialScan);

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
