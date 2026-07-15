namespace GameSaveManager.Application.Discovery;

public sealed record ManifestUpdateStatus(string Version, DateTimeOffset? UpdatedAt, string? ETag, string? Sha256);

public interface IManifestUpdateService
{
    Task<ManifestUpdateStatus> GetStatusAsync(CancellationToken cancellationToken);
    Task<ManifestUpdateStatus> UpdateAsync(CancellationToken cancellationToken);
}
