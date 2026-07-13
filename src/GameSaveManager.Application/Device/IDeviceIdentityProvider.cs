namespace GameSaveManager.Application.Device;

/// <summary>提供本机稳定 deviceId；首次启动生成后必须持久化复用。</summary>
public interface IDeviceIdentityProvider
{
    Task<string> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken);
}
