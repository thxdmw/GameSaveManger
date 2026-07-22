namespace GameSaveManager.Application.Discovery;

/// <summary>用于跨商店与本地安装来源识别同一款游戏的稳定身份。</summary>
public sealed record GameIdentity(
    string Name,
    string Provider,
    string? ProviderGameId,
    string InstallDirectory,
    string? ExecutablePath,
    string? ProcessName)
{
    public const string Steam = "STEAM";
    public const string Epic = "EPIC";
    public const string Gog = "GOG";
    public const string Local = "LOCAL";
    public const string Custom = "CUSTOM";
}
