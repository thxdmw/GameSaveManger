namespace GameSaveManager.Infrastructure.Updates;

public sealed record AuthenticodeVerificationResult(
    bool Trusted,
    string? PublisherCertificateSha256,
    string? Error);

/// <summary>按 Windows Authenticode 默认应用策略验证可执行文件及发布者证书。</summary>
public interface IAuthenticodeVerifier
{
    AuthenticodeVerificationResult Verify(string filePath);
}
