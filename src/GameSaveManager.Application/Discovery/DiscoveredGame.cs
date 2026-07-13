namespace GameSaveManager.Application.Discovery;

/// <summary>从本机游戏平台发现的安装信息；存档路径仍需用户确认，不能靠猜测覆盖。</summary>
public sealed record DiscoveredGame(
    string Name,
    string Provider,
    string ProviderGameId,
    string InstallDirectory,
    string? ExecutablePath,
    string? ProcessName);