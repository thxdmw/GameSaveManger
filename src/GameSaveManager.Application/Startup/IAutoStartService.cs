namespace GameSaveManager.Application.Startup;

/// <summary>管理当前用户的 Windows 开机自启项，不写入系统级注册表。</summary>
public interface IAutoStartService
{
    bool IsEnabled();

    Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken);
}