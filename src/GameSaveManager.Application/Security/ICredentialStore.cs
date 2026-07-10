namespace GameSaveManager.Application.Security;

/// <summary>敏感凭据存储抽象；设备 Token 禁止写入 SQLite、JSON 或普通配置文件。</summary>
public interface ICredentialStore
{
    Task SaveAsync(string target, string secret, CancellationToken cancellationToken);

    Task<string?> ReadAsync(string target, CancellationToken cancellationToken);

    Task DeleteAsync(string target, CancellationToken cancellationToken);
}
