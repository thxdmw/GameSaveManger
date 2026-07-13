namespace GameSaveManager.Application.Discovery;

/// <summary>读取本机游戏平台公开安装信息的发现服务。</summary>
public interface IGameDiscoveryService
{
    Task<IReadOnlyList<DiscoveredGame>> DiscoverAsync(CancellationToken cancellationToken);
}