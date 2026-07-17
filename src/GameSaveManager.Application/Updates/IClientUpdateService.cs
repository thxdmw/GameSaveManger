namespace GameSaveManager.Application.Updates;

/// <summary>从可信发布源检查、下载并校验客户端更新；安装前必须由界面再次确认。</summary>
public interface IClientUpdateService
{
    Task<ClientUpdateRelease?> CheckForUpdateAsync(
        string currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken);

    Task<PreparedClientUpdate> DownloadUpdateAsync(
        ClientUpdateRelease release,
        IProgress<ClientUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken);

    void LaunchInstaller(PreparedClientUpdate update);
}
