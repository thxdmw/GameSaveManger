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
        EnsureSafeManifestPath(DirectoryPath);
        Directory.CreateDirectory(DirectoryPath);
        EnsureSafeManifestPath(DirectoryPath);
        Metadata old = await ReadMetadataAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Get, Url);
        if (!string.IsNullOrWhiteSpace(old.ETag)) request.Headers.TryAddWithoutValidation("If-None-Match", old.ETag);
        using HttpResponseMessage response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotModified) return old.ToStatus();
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaximumManifestBytes) throw new InvalidDataException("下载的 Ludusavi Manifest 超过大小上限。");
        string suffix = "." + Guid.NewGuid().ToString("N") + ".tmp";
        string temporary = ManifestPath + suffix;
        string metadataTemporary = MetadataPath + suffix;
        EnsureSafeManifestPath(temporary);
        EnsureSafeManifestPath(metadataTemporary);
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
            TryDeleteTemporary(temporary);
            TryDeleteTemporary(metadataTemporary);
        }
    }

    private static void TryDeleteTemporary(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { }
    }

    private static void EnsureSafeManifestPath(string path)
    {
        string boundary = Path.TrimEndingDirectorySeparator(Path.GetFullPath(AppDataPaths.RootDirectory));
        string target = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        string relative = Path.GetRelativePath(boundary, target);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("Ludusavi Manifest 路径越过应用数据边界。");

        string current = boundary;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries).Prepend(string.Empty))
        {
            if (segment.Length > 0) current = Path.Combine(current, segment);
            if ((Directory.Exists(current) || File.Exists(current))
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Ludusavi Manifest 路径包含重解析点。");
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
