using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameSaveManager.Application.Updates;
using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.Infrastructure.Updates;

/// <summary>从项目公开 GitHub Releases 获取并校验 Windows 安装包。</summary>
public sealed class GitHubClientUpdateService : IClientUpdateService
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/thxdmw/GameSaveManger/releases?per_page=30";
    private const long MaximumApiResponseBytes = 2 * 1024 * 1024;
    private const long MaximumChecksumBytes = 64 * 1024;
    private const long MaximumInstallerBytes = 1024L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _downloadRoot;

    public GitHubClientUpdateService(HttpClient httpClient, string? downloadRoot = null)
    {
        _httpClient = httpClient;
        _downloadRoot = Path.GetFullPath(downloadRoot ?? AppDataPaths.UpdateDirectory);
    }

    public async Task<ClientUpdateRelease?> CheckForUpdateAsync(
        string currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken)
    {
        SemanticVersion current = SemanticVersion.Parse(currentVersion);
        using HttpRequestMessage request = CreateRequest(new Uri(ReleasesEndpoint));
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        EnsureContentLength(response, MaximumApiResponseBytes, "发布列表");
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        if (Encoding.UTF8.GetByteCount(json) > MaximumApiResponseBytes)
            throw new InvalidDataException("GitHub 发布列表超过客户端允许的大小。");

        GitHubReleaseDto[] releases = JsonSerializer.Deserialize<GitHubReleaseDto[]>(json)
            ?? [];
        return releases
            .Where(release => !release.Draft && (includePrerelease || !release.Prerelease))
            .Select(TryCreateRelease)
            .Where(candidate => candidate is not null
                && SemanticVersion.Parse(candidate.Version).CompareTo(current) > 0)
            .OrderByDescending(candidate => SemanticVersion.Parse(candidate!.Version))
            .FirstOrDefault();
    }

    public async Task<PreparedClientUpdate> DownloadUpdateAsync(
        ClientUpdateRelease release,
        IProgress<ClientUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRelease(release);
        byte[] checksumBytes = await DownloadBytesAsync(
            release.Checksums,
            MaximumChecksumBytes,
            cancellationToken);
        string checksumAssetHash = Convert.ToHexString(SHA256.HashData(checksumBytes)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(checksumAssetHash),
            Convert.FromHexString(release.Checksums.Sha256)))
            throw new InvalidDataException("SHA256SUMS.txt 与 GitHub 发布资产摘要不一致。");

        string expectedInstallerHash = ParseInstallerChecksum(
            Encoding.UTF8.GetString(checksumBytes),
            release.Installer.Name);
        if (!string.Equals(expectedInstallerHash, release.Installer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装包摘要与 SHA256SUMS.txt 不一致，已阻止下载。");

        string versionDirectory = GetSafeVersionDirectory(release.Version);
        Directory.CreateDirectory(versionDirectory);
        string installerPath = Path.Combine(versionDirectory, release.Installer.Name);
        if (File.Exists(installerPath)
            && string.Equals(await ComputeSha256Async(installerPath, cancellationToken), expectedInstallerHash, StringComparison.OrdinalIgnoreCase))
        {
            progress?.Report(new ClientUpdateDownloadProgress(release.Installer.Size, release.Installer.Size));
            return new PreparedClientUpdate(release, installerPath, expectedInstallerHash);
        }

        string temporaryPath = installerPath + ".download";
        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            await DownloadInstallerAsync(release.Installer, temporaryPath, progress, cancellationToken);
            string actualHash = await ComputeSha256Async(temporaryPath, cancellationToken);
            if (!string.Equals(actualHash, expectedInstallerHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载的安装包 SHA-256 校验失败，文件已删除。");
            File.Move(temporaryPath, installerPath, true);
            return new PreparedClientUpdate(release, installerPath, actualHash);
        }
        catch
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            throw;
        }
    }

    public void LaunchInstaller(PreparedClientUpdate update)
    {
        string installerPath = Path.GetFullPath(update.InstallerPath);
        string rootWithSeparator = _downloadRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!installerPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(installerPath))
            throw new InvalidOperationException("待安装文件不在受控更新目录中。");

        using FileStream stream = File.OpenRead(installerPath);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, update.VerifiedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装前再次校验失败，更新文件可能已被修改。");

        Process.Start(new ProcessStartInfo
        {
            FileName = installerPath,
            UseShellExecute = true,
            WorkingDirectory = Path.GetDirectoryName(installerPath)
                ?? throw new InvalidOperationException("更新目录无效。")
        });
    }

    private ClientUpdateRelease? TryCreateRelease(GitHubReleaseDto release)
    {
        if (!SemanticVersion.TryParse(release.TagName, out SemanticVersion? version)
            || version is null
            || release.PublishedAt is null
            || release.Assets is null
            || !TryCreateTrustedUri(release.HtmlUrl, "github.com", out Uri? releasePage)) return null;

        string normalizedVersion = version.ToString();
        GitHubAssetDto? installer = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, $"GameSaveManager-Setup-{normalizedVersion}.exe", StringComparison.Ordinal));
        GitHubAssetDto? checksums = release.Assets.FirstOrDefault(asset =>
            string.Equals(asset.Name, "SHA256SUMS.txt", StringComparison.Ordinal));
        if (!TryCreateAsset(installer, out ClientUpdateAsset? installerAsset)
            || !TryCreateAsset(checksums, out ClientUpdateAsset? checksumAsset)) return null;

        return new ClientUpdateRelease(
            normalizedVersion,
            release.Prerelease,
            releasePage!,
            release.Body ?? string.Empty,
            release.PublishedAt.Value,
            installerAsset!,
            checksumAsset!);
    }

    private static bool TryCreateAsset(GitHubAssetDto? asset, out ClientUpdateAsset? result)
    {
        result = null;
        if (asset is null) return false;
        if (!string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase)
            || asset.Size <= 0
            || asset.Size > MaximumInstallerBytes
            || !TryCreateTrustedUri(asset.BrowserDownloadUrl, "github.com", out Uri? uri)
            || !uri!.AbsolutePath.StartsWith("/thxdmw/GameSaveManger/releases/download/", StringComparison.OrdinalIgnoreCase)
            || !TryNormalizeSha256(asset.Digest, out string? digest)) return false;
        result = new ClientUpdateAsset(asset.Name, uri!, asset.Size, digest!);
        return true;
    }

    private static void ValidateRelease(ClientUpdateRelease release)
    {
        if (!SemanticVersion.TryParse(release.Version, out _)
            || !TryCreateTrustedUri(release.ReleasePageUri.AbsoluteUri, "github.com", out _)
            || !TryNormalizeSha256(release.Installer.Sha256, out _)
            || !TryNormalizeSha256(release.Checksums.Sha256, out _))
            throw new InvalidDataException("更新发布信息不完整或不可信。");
    }

    private async Task<byte[]> DownloadBytesAsync(
        ClientUpdateAsset asset,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(asset.DownloadUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        EnsureContentLength(response, maximumBytes, asset.Name);
        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var buffer = new MemoryStream();
        await stream.CopyToAsync(buffer, cancellationToken);
        if (buffer.Length > maximumBytes) throw new InvalidDataException($"{asset.Name} 超过允许的大小。");
        return buffer.ToArray();
    }

    private async Task DownloadInstallerAsync(
        ClientUpdateAsset asset,
        string destination,
        IProgress<ClientUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(asset.DownloadUri);
        using HttpResponseMessage response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        EnsureContentLength(response, MaximumInstallerBytes, asset.Name);
        long total = response.Content.Headers.ContentLength ?? asset.Size;
        if (asset.Size > 0 && total != asset.Size)
            throw new InvalidDataException("安装包大小与 GitHub 发布信息不一致。");

        await using Stream source = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var target = new FileStream(
            destination,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] buffer = new byte[128 * 1024];
        long received = 0;
        while (true)
        {
            int count = await source.ReadAsync(buffer, cancellationToken);
            if (count == 0) break;
            received += count;
            if (received > MaximumInstallerBytes || received > asset.Size)
                throw new InvalidDataException("安装包下载大小超过发布信息声明值。");
            await target.WriteAsync(buffer.AsMemory(0, count), cancellationToken);
            progress?.Report(new ClientUpdateDownloadProgress(received, asset.Size));
        }
        await target.FlushAsync(cancellationToken);
        if (received != asset.Size) throw new InvalidDataException("安装包下载不完整。");
    }

    private string GetSafeVersionDirectory(string version)
    {
        if (!SemanticVersion.TryParse(version, out SemanticVersion? parsed) || parsed is null)
            throw new InvalidDataException("更新版本号无效。");
        string path = Path.GetFullPath(Path.Combine(_downloadRoot, parsed.ToString()));
        string rootWithSeparator = _downloadRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新目录越界。");
        return path;
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("GameSaveManager-UpdateClient/0.1");
        request.Headers.TryAddWithoutValidation("X-GitHub-Api-Version", "2026-03-10");
        return request;
    }

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            128 * 1024,
            FileOptions.Asynchronous | FileOptions.SequentialScan);
        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string ParseInstallerChecksum(string content, string installerName)
    {
        foreach (string rawLine in content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            string line = rawLine.Trim();
            if (line.Length < 66) continue;
            string hash = line[..64];
            string name = line[64..].TrimStart();
            if (name.StartsWith('*')) name = name[1..];
            if (string.Equals(name, installerName, StringComparison.Ordinal)
                && TryNormalizeSha256(hash, out string? normalized)) return normalized!;
        }
        throw new InvalidDataException($"SHA256SUMS.txt 未包含 {installerName} 的有效摘要。");
    }

    private static void EnsureContentLength(HttpResponseMessage response, long maximumBytes, string name)
    {
        if (response.Content.Headers.ContentLength is > 0 and var length && length > maximumBytes)
            throw new InvalidDataException($"{name} 超过客户端允许的大小。");
    }

    private static bool TryNormalizeSha256(string? value, out string? normalized)
    {
        normalized = value?.Trim();
        if (normalized?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true)
            normalized = normalized[7..];
        if (normalized is null || normalized.Length != 64 || normalized.Any(character => !Uri.IsHexDigit(character)))
        {
            normalized = null;
            return false;
        }
        normalized = normalized.ToLowerInvariant();
        return true;
    }

    private static bool TryCreateTrustedUri(string? value, string expectedHost, out Uri? uri)
    {
        bool valid = Uri.TryCreate(value, UriKind.Absolute, out uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && string.Equals(uri.Host, expectedHost, StringComparison.OrdinalIgnoreCase)
            && string.IsNullOrEmpty(uri.UserInfo);
        if (!valid) uri = null;
        return valid;
    }

    private sealed record GitHubReleaseDto(
        [property: JsonPropertyName("tag_name")] string TagName,
        [property: JsonPropertyName("html_url")] string HtmlUrl,
        [property: JsonPropertyName("body")] string? Body,
        [property: JsonPropertyName("draft")] bool Draft,
        [property: JsonPropertyName("prerelease")] bool Prerelease,
        [property: JsonPropertyName("published_at")] DateTimeOffset? PublishedAt,
        [property: JsonPropertyName("assets")] GitHubAssetDto[]? Assets);

    private sealed record GitHubAssetDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("state")] string State,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("digest")] string? Digest,
        [property: JsonPropertyName("browser_download_url")] string BrowserDownloadUrl);
}
