namespace GameSaveManager.Application.Games;

/// <summary>本机游戏配置持久化抽象，供界面在登录后恢复存档和监控设置。</summary>
public interface ILocalGameProfileStore
{
    Task<LocalGameProfile?> GetAsync(string serverKey, string gameId, CancellationToken cancellationToken);

    Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken);
}