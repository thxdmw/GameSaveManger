namespace GameSaveManager.Application.Games;

/// <summary>按服务端隔离的本机游戏配置，不包含任何账号凭据。</summary>
public sealed record LocalGameProfile(
    string ServerKey,
    string GameId,
    string SaveDirectory,
    string ProcessName,
    bool AutoSnapshotEnabled);