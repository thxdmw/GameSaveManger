using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.Infrastructure.Discovery;

/// <summary>使用 ETag 原子更新 Manifest；任何失败都会保留当前可用文件。</summary>
public sealed class LudusaviManifestUpdateService(HttpClient client) : IManifestUpdateService
{
    private const string Url = "https://raw.githubusercontent.com/mtkennerly/ludusavi-manifest/master/data/manifest.yaml";
    private static readonly string DirectoryPath = Path.Combine(AppDataPaths.RootDirectory, "manifest");
    private static readonly string ManifestPath = Path.Combine(DirectoryPath, "ludusavi-manifest.yaml");
    private static readonly string MetadataPath = Path.Combine(DirectoryPath, "metadata.json");
    private const long MaximumManifestBytes = 64L * 1024 * 1024;

    public async Task<ManifestUpdateStatus> GetStatusAsync(CancellationToken cancellationToken)
    {
        Metadata metadata = await ReadMetadataAsync(cancellationToken);
        return new ManifestUpdateStatus(metadata.Version, metadata.UpdatedAt, metadata.ETag, metadata.Sha256);
    }

    public async Task<ManifestUpdateStatus> UpdateAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(DirectoryPath);
        Metadata old = await ReadMetadataAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        if (!string.IsNullOrWhiteSpace(old.ETag)) request.Headers.TryAddWithoutValidation("If-None-Match", old.ETag);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified) return old.ToStatus();
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumManifestBytes) throw new InvalidDataException("下载的 Ludusavi Manifest 超过大小上限。");
        string temporary = ManifestPath + ".tmp";
        string metadataTemporary = MetadataPath + ".tmp";
        try
        {
            await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using (FileStream output = new(temporary, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaximumManifestBytes) throw new InvalidDataException("下载的 Ludusavi Manifest 超过大小上限。");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
            }
            LudusaviManifestDetector.ValidateManifestFile(temporary);
            string hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(temporary, cancellationToken))).ToLowerInvariant();
            var current = new Metadata(hash[..12], DateTimeOffset.UtcNow, response.Headers.ETag?.Tag, hash);
            await File.WriteAllTextAsync(metadataTemporary, JsonSerializer.Serialize(current), cancellationToken);
            File.Move(temporary, ManifestPath, true);
            File.Move(metadataTemporary, MetadataPath, true);
            LudusaviManifestDetector.Invalidate();
            return current.ToStatus();
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
            if (File.Exists(metadataTemporary)) File.Delete(metadataTemporary);
        }
    }
    private static async Task<Metadata> ReadMetadataAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(MetadataPath)) return new Metadata("内置版本", null, null, null);
        try { return JsonSerializer.Deserialize<Metadata>(await File.ReadAllTextAsync(MetadataPath, cancellationToken)) ?? new Metadata("内置版本", null, null, null); }
        catch (JsonException) { return new Metadata("内置版本", null, null, null); }
    }

    private sealed record Metadata(string Version, DateTimeOffset? UpdatedAt, string? ETag, string? Sha256)
    { public ManifestUpdateStatus ToStatus() => new(Version, UpdatedAt, ETag, Sha256); }
}
