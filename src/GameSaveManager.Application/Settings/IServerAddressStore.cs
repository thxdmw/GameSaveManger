namespace GameSaveManager.Application.Settings;

/// <summary>保存不含凭据的客户端服务器地址设置。</summary>
public interface IServerAddressStore
{
    Task<string?> ReadAsync(CancellationToken cancellationToken);
    Task SaveAsync(string serverAddress, CancellationToken cancellationToken);
}