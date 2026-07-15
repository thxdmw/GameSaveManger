namespace GameSaveManager.Application.Discovery;

/// <summary>?????????????????????????????????</summary>
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
