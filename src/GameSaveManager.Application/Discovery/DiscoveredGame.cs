namespace GameSaveManager.Application.Discovery;

/// <summary>从本机游戏平台发现的安装信息；存档路径仍需用户确认，不能靠猜测覆盖。</summary>
public sealed record DiscoveredGame(GameIdentity Identity)
{
    public string Name => Identity.Name;
    public string Provider => Identity.Provider;
    public string? ProviderGameId => Identity.ProviderGameId;
    public string InstallDirectory => Identity.InstallDirectory;
    public string? ExecutablePath => Identity.ExecutablePath;
    public string? ProcessName => Identity.ProcessName;
}
