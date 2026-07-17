namespace GameSaveManager.Application.Updates;

public sealed record ClientUpdateAsset(
    string Name,
    Uri DownloadUri,
    long Size,
    string Sha256);

public sealed record ClientUpdateRelease(
    string Version,
    bool Prerelease,
    Uri ReleasePageUri,
    string ReleaseNotes,
    DateTimeOffset PublishedAt,
    ClientUpdateAsset Installer,
    ClientUpdateAsset Checksums);

public sealed record ClientUpdateDownloadProgress(long BytesReceived, long TotalBytes)
{
    public double Percentage => TotalBytes <= 0
        ? 0
        : Math.Clamp(BytesReceived * 100d / TotalBytes, 0, 100);
}

public sealed record PreparedClientUpdate(
    ClientUpdateRelease Release,
    string InstallerPath,
    string VerifiedSha256);

public sealed record ClientUpdatePreferences(
    bool CheckOnStartup,
    DateTimeOffset? LastCheckedAtUtc,
    string? LastAvailableVersion)
{
    public static ClientUpdatePreferences Default { get; } = new(true, null, null);
}
