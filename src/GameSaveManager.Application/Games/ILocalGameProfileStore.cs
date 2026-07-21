namespace GameSaveManager.Application.Games;

/// <summary>本机游戏配置持久化抽象，供界面在登录后恢复存档和监控设置。</summary>
public interface ILocalGameProfileStore
{
    Task<LocalGameProfile?> GetAsync(string serverKey, string userId, string gameId, CancellationToken cancellationToken);

    Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken);

    /// <summary>读取指定服务端下所有本机游戏配置，用于启动时恢复自动同步。</summary>
    Task<IReadOnlyList<LocalGameProfile>> ListAsync(string serverKey, string userId, CancellationToken cancellationToken);

    /// <summary>把升级前没有账号列的配置安全归入当前账号拥有的游戏。</summary>
    Task ClaimLegacyAsync(string serverKey, string userId, IReadOnlyCollection<string> ownedGameIds, CancellationToken cancellationToken);

    /// <summary>删除已从云端游戏库移除的本机同步配置。</summary>
    Task DeleteAsync(string serverKey, string userId, string gameId, CancellationToken cancellationToken);
}
