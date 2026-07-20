using System.Diagnostics;
using System.Net.Http.Headers;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameSaveManager.Application.Updates;
using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.Infrastructure.Updates;

/// <summary>只接受项目签名清单和受信 Authenticode 发布者共同授权的 Windows 更新。</summary>
public sealed class GitHubClientUpdateService : IClientUpdateService
{
    private const string ReleasesEndpoint = "https://api.github.com/repos/thxdmw/GameSaveManger/releases?per_page=30";
    private const string PublicKeyResourcePrefix = "GameSaveManager.Infrastructure.Updates.Assets.";
    private const long MaximumApiResponseBytes = 2 * 1024 * 1024;
    private const long MaximumManifestBytes = 256 * 1024;
    private const long MaximumSignatureBytes = 16 * 1024;
    private const long MaximumChecksumBytes = 64 * 1024;
    private const long MaximumInstallerBytes = 1024L * 1024 * 1024;
    private readonly HttpClient _httpClient;
    private readonly string _downloadRoot;
    private readonly IAuthenticodeVerifier _authenticodeVerifier;
    private readonly string[] _manifestPublicKeys;

    public GitHubClientUpdateService(
        HttpClient httpClient,
        string? downloadRoot = null,
        IAuthenticodeVerifier? authenticodeVerifier = null,
        string? manifestPublicKeyPem = null)
    {
        _httpClient = httpClient;
        _downloadRoot = Path.GetFullPath(downloadRoot ?? AppDataPaths.UpdateDirectory);
        _authenticodeVerifier = authenticodeVerifier ?? new WindowsAuthenticodeVerifier();
        _manifestPublicKeys = manifestPublicKeyPem is null
            ? LoadEmbeddedPublicKeys()
            : [manifestPublicKeyPem];
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

        GitHubReleaseDto[] releases = JsonSerializer.Deserialize<GitHubReleaseDto[]>(json) ?? [];
        ReleaseCandidate? candidate = releases
            .Where(release => !release.Draft && (includePrerelease || !release.Prerelease))
            .Select(TryCreateCandidate)
            .Where(item => item is not null && item.Version.CompareTo(current) > 0)
            .OrderByDescending(item => item!.Version)
            .FirstOrDefault();
        return candidate is null
            ? null
            : await CreateVerifiedReleaseAsync(candidate, cancellationToken);
    }

    public async Task<PreparedClientUpdate> DownloadUpdateAsync(
        ClientUpdateRelease release,
        string currentVersion,
        IProgress<ClientUpdateDownloadProgress>? progress,
        CancellationToken cancellationToken)
    {
        ValidateRelease(release);
        SemanticVersion.Parse(currentVersion);
        byte[] checksumBytes = await DownloadAndVerifyAssetAsync(
            release.Checksums,
            MaximumChecksumBytes,
            cancellationToken);
        string expectedInstallerHash = ParseInstallerChecksum(
            Encoding.UTF8.GetString(checksumBytes),
            release.Installer.Name);
        if (!string.Equals(expectedInstallerHash, release.Installer.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("签名清单、GitHub 资产摘要与 SHA256SUMS.txt 不一致，已阻止下载。");

        string versionDirectory = GetSafeVersionDirectory(release.Version);
        Directory.CreateDirectory(versionDirectory);
        string installerPath = Path.Combine(versionDirectory, release.Installer.Name);
        if (File.Exists(installerPath)
            && string.Equals(await ComputeSha256Async(installerPath, cancellationToken), expectedInstallerHash, StringComparison.OrdinalIgnoreCase))
        {
            VerifyInstallerAuthenticode(installerPath, release);
            progress?.Report(new ClientUpdateDownloadProgress(release.Installer.Size, release.Installer.Size));
            return new PreparedClientUpdate(release, currentVersion, installerPath, expectedInstallerHash);
        }

        string temporaryPath = installerPath + ".download";
        try
        {
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
            await DownloadInstallerAsync(release.Installer, temporaryPath, progress, cancellationToken);
            string actualHash = await ComputeSha256Async(temporaryPath, cancellationToken);
            if (!string.Equals(actualHash, expectedInstallerHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("下载的安装包 SHA-256 校验失败，文件已删除。");
            VerifyInstallerAuthenticode(temporaryPath, release);
            File.Move(temporaryPath, installerPath, true);
            return new PreparedClientUpdate(release, currentVersion, installerPath, actualHash);
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
        EnsureWithinDownloadRoot(installerPath);
        if (!File.Exists(installerPath)) throw new InvalidOperationException("待安装更新文件不存在。");
        using FileStream stream = File.OpenRead(installerPath);
        string actualHash = Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
        if (!string.Equals(actualHash, update.VerifiedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("安装前再次校验失败，更新文件可能已被修改。");
        VerifyInstallerAuthenticode(installerPath, update.Release);

        string applicationPath = Environment.ProcessPath
            ?? throw new InvalidOperationException("无法确定当前客户端路径。");
        string bootstrapperSource = Path.Combine(AppContext.BaseDirectory, "GameSaveManager.UpdateBootstrapper.exe");
        string currentPublisherHash = VerifyInstalledBootstrapper(applicationPath, bootstrapperSource);
        if (!SemanticVersion.TryParse(update.PreviousVersion, out SemanticVersion? previousVersion) || previousVersion is null)
            throw new InvalidDataException("上一版本号无效，无法创建更新事务。");

        Directory.CreateDirectory(AppDataPaths.UpdateTransactionDirectory);
        Directory.CreateDirectory(AppDataPaths.RollbackInstallerDirectory);
        string transactionId = Guid.NewGuid().ToString("N");
        string healthToken = Guid.NewGuid().ToString("N");
        string bootstrapperPath = Path.Combine(
            AppDataPaths.UpdateTransactionDirectory,
            $"bootstrapper-{transactionId}.exe");
        File.Copy(bootstrapperSource, bootstrapperPath, false);
        VerifyInstalledBootstrapper(applicationPath, bootstrapperPath);

        string transactionPath = Path.Combine(AppDataPaths.UpdateTransactionDirectory, $"transaction-{transactionId}.state");
        string healthPath = Path.Combine(AppDataPaths.UpdateTransactionDirectory, $"health-{healthToken}.ready");
        string rollbackInstallerPath = Path.Combine(
            AppDataPaths.RollbackInstallerDirectory,
            $"GameSaveManager-Setup-{previousVersion}.exe");
        var startInfo = new ProcessStartInfo
        {
            FileName = bootstrapperPath,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = AppDataPaths.UpdateTransactionDirectory
        };
        AddArgument(startInfo, "--wait-pid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        AddArgument(startInfo, "--new-installer", installerPath);
        AddArgument(startInfo, "--new-sha256", update.VerifiedSha256);
        AddArgument(startInfo, "--new-publisher-sha256", update.Release.PublisherCertificateSha256);
        AddArgument(startInfo, "--previous-installer", rollbackInstallerPath);
        AddArgument(startInfo, "--previous-publisher-sha256", currentPublisherHash);
        AddArgument(startInfo, "--app", applicationPath);
        AddArgument(startInfo, "--transaction", transactionPath);
        AddArgument(startInfo, "--health-file", healthPath);
        AddArgument(startInfo, "--health-token", healthToken);
        AddArgument(startInfo, "--from-version", previousVersion.ToString());
        AddArgument(startInfo, "--to-version", update.Release.Version);
        if (Process.Start(startInfo) is null)
            throw new InvalidOperationException("无法启动安全更新程序。");
    }

    private string VerifyInstalledBootstrapper(string applicationPath, string bootstrapperPath)
    {
        if (!File.Exists(bootstrapperPath))
            throw new FileNotFoundException("客户端缺少安全更新程序。", bootstrapperPath);
        AuthenticodeVerificationResult application = _authenticodeVerifier.Verify(applicationPath);
        AuthenticodeVerificationResult bootstrapper = _authenticodeVerifier.Verify(bootstrapperPath);
        if (!application.Trusted || !bootstrapper.Trusted ||
            string.IsNullOrWhiteSpace(application.PublisherCertificateSha256) ||
            !string.Equals(
                application.PublisherCertificateSha256,
                bootstrapper.PublisherCertificateSha256,
                StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("安全更新程序与当前客户端的受信发布者不一致。");
        return application.PublisherCertificateSha256.ToLowerInvariant();
    }

    private static void AddArgument(ProcessStartInfo startInfo, string name, string value)
    {
        startInfo.ArgumentList.Add(name);
        startInfo.ArgumentList.Add(value);
    }

    private async Task<ClientUpdateRelease> CreateVerifiedReleaseAsync(
        ReleaseCandidate candidate,
        CancellationToken cancellationToken)
    {
        GitHubAssetDto[] assets = candidate.Release.Assets
            ?? throw new InvalidDataException($"版本 {candidate.Version} 没有发布资产。");
        ClientUpdateAsset manifestAsset = CreateRequiredAsset(assets, "update-manifest.json");
        ClientUpdateAsset signatureAsset = CreateRequiredAsset(assets, "update-manifest.json.sig");
        ClientUpdateAsset installerAsset = CreateRequiredAsset(
            assets,
            $"GameSaveManager-Setup-{candidate.Version}.exe");
        ClientUpdateAsset checksumAsset = CreateRequiredAsset(assets, "SHA256SUMS.txt");

        byte[] manifestBytes = await DownloadAndVerifyAssetAsync(
            manifestAsset,
            MaximumManifestBytes,
            cancellationToken);
        byte[] signatureText = await DownloadAndVerifyAssetAsync(
            signatureAsset,
            MaximumSignatureBytes,
            cancellationToken);
        VerifyManifestSignature(manifestBytes, signatureText);
        SignedUpdateManifestDto manifest = JsonSerializer.Deserialize<SignedUpdateManifestDto>(
            manifestBytes,
            new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow })
            ?? throw new InvalidDataException("签名更新清单为空。");
        ValidateManifest(candidate, manifest, installerAsset);

        return new ClientUpdateRelease(
            candidate.Version.ToString(),
            candidate.Release.Prerelease,
            candidate.ReleasePage,
            candidate.Release.Body ?? string.Empty,
            manifest.PublishedAtUtc,
            installerAsset,
            checksumAsset,
            NormalizeSha256(manifest.Installer.PublisherCertificateSha256));
    }

    private static ReleaseCandidate? TryCreateCandidate(GitHubReleaseDto release)
    {
        if (!SemanticVersion.TryParse(release.TagName, out SemanticVersion? version)
            || version is null
            || release.PublishedAt is null
            || !TryCreateTrustedUri(release.HtmlUrl, "github.com", out Uri? releasePage)) return null;
        return new ReleaseCandidate(release, version, releasePage!);
    }

    private static ClientUpdateAsset CreateRequiredAsset(GitHubAssetDto[] assets, string name)
    {
        GitHubAssetDto? asset = assets.FirstOrDefault(item => string.Equals(item.Name, name, StringComparison.Ordinal));
        if (!TryCreateAsset(asset, out ClientUpdateAsset? result))
            throw new InvalidDataException($"发布缺少有效资产：{name}。");
        return result!;
    }

    private static bool TryCreateAsset(GitHubAssetDto? asset, out ClientUpdateAsset? result)
    {
        result = null;
        if (asset is null
            || !string.Equals(asset.State, "uploaded", StringComparison.OrdinalIgnoreCase)
            || asset.Size <= 0
            || asset.Size > MaximumInstallerBytes
            || !TryCreateTrustedUri(asset.BrowserDownloadUrl, "github.com", out Uri? uri)
            || !uri!.AbsolutePath.StartsWith("/thxdmw/GameSaveManger/releases/download/", StringComparison.OrdinalIgnoreCase)
            || !TryNormalizeSha256(asset.Digest, out string? digest)) return false;
        result = new ClientUpdateAsset(asset.Name, uri!, asset.Size, digest!);
        return true;
    }

    private void VerifyManifestSignature(byte[] manifest, byte[] signatureText)
    {
        byte[] signature;
        try
        {
            signature = Convert.FromBase64String(Encoding.ASCII.GetString(signatureText).Trim());
        }
        catch (FormatException exception)
        {
            throw new InvalidDataException("更新清单签名格式无效。", exception);
        }
        bool valid = _manifestPublicKeys.Any(publicKeyPem => VerifyWithPublicKey(publicKeyPem, manifest, signature));
        if (!valid) throw new CryptographicException("更新清单签名验证失败。");
    }

    private static bool VerifyWithPublicKey(string publicKeyPem, byte[] manifest, byte[] signature)
    {
        try
        {
            using ECDsa publicKey = ECDsa.Create();
            publicKey.ImportFromPem(publicKeyPem);
            return publicKey.VerifyData(
                manifest,
                signature,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static void ValidateManifest(
        ReleaseCandidate candidate,
        SignedUpdateManifestDto manifest,
        ClientUpdateAsset installerAsset)
    {
        string expectedChannel = candidate.Release.Prerelease ? "preview" : "stable";
        if (manifest.SchemaVersion != 1
            || !string.Equals(manifest.Product, "GameSaveManager", StringComparison.Ordinal)
            || !string.Equals(manifest.Version, candidate.Version.ToString(), StringComparison.Ordinal)
            || !string.Equals(manifest.Channel, expectedChannel, StringComparison.Ordinal)
            || !Uri.TryCreate(manifest.ReleasePageUrl, UriKind.Absolute, out Uri? releasePage)
            || releasePage != candidate.ReleasePage
            || manifest.PublishedAtUtc == default
            || Math.Abs((manifest.PublishedAtUtc - candidate.Release.PublishedAt!.Value).TotalHours) > 24
            || manifest.Installer is null
            || !string.Equals(manifest.Installer.Name, installerAsset.Name, StringComparison.Ordinal)
            || !Uri.TryCreate(manifest.Installer.Url, UriKind.Absolute, out Uri? installerUri)
            || installerUri != installerAsset.DownloadUri
            || manifest.Installer.Size != installerAsset.Size
            || !string.Equals(NormalizeSha256(manifest.Installer.Sha256), installerAsset.Sha256, StringComparison.Ordinal)
            || !TryNormalizeSha256(manifest.Installer.PublisherCertificateSha256, out _))
            throw new InvalidDataException("签名更新清单与 GitHub 发布信息不一致。");
    }

    private static void ValidateRelease(ClientUpdateRelease release)
    {
        if (!SemanticVersion.TryParse(release.Version, out _)
            || !TryCreateTrustedUri(release.ReleasePageUri.AbsoluteUri, "github.com", out _)
            || !TryNormalizeSha256(release.Installer.Sha256, out _)
            || !TryNormalizeSha256(release.Checksums.Sha256, out _)
            || !TryNormalizeSha256(release.PublisherCertificateSha256, out _))
            throw new InvalidDataException("更新发布信息不完整或不可信。");
    }

    private void VerifyInstallerAuthenticode(string installerPath, ClientUpdateRelease release)
    {
        AuthenticodeVerificationResult verification = _authenticodeVerifier.Verify(installerPath);
        if (!verification.Trusted)
            throw new CryptographicException($"安装包 Authenticode 验证失败：{verification.Error ?? "未知错误"}");
        if (!string.Equals(
            verification.PublisherCertificateSha256,
            release.PublisherCertificateSha256,
            StringComparison.OrdinalIgnoreCase))
            throw new CryptographicException("安装包发布者证书与签名更新清单不一致。");
    }

    private async Task<byte[]> DownloadAndVerifyAssetAsync(
        ClientUpdateAsset asset,
        long maximumBytes,
        CancellationToken cancellationToken)
    {
        byte[] content = await DownloadBytesAsync(asset, maximumBytes, cancellationToken);
        string actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!CryptographicOperations.FixedTimeEquals(
            Convert.FromHexString(actual),
            Convert.FromHexString(asset.Sha256)))
            throw new InvalidDataException($"{asset.Name} 与 GitHub 资产摘要不一致。");
        return content;
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
            throw new InvalidDataException("安装包大小与签名更新清单不一致。");

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
        EnsureWithinDownloadRoot(path);
        return path;
    }

    private void EnsureWithinDownloadRoot(string path)
    {
        string rootWithSeparator = _downloadRoot.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!path.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("更新目录越界。");
    }

    private static HttpRequestMessage CreateRequest(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        request.Headers.UserAgent.ParseAdd("GameSaveManager-UpdateClient/0.2");
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

    private static string NormalizeSha256(string value) =>
        TryNormalizeSha256(value, out string? normalized)
            ? normalized!
            : throw new InvalidDataException("SHA-256 格式无效。");

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

    private static string[] LoadEmbeddedPublicKeys()
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        string[] resources = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(PublicKeyResourcePrefix, StringComparison.Ordinal) &&
                           name.EndsWith(".pem", StringComparison.OrdinalIgnoreCase))
            .Order(StringComparer.Ordinal)
            .ToArray();
        if (resources.Length == 0)
            throw new InvalidOperationException("客户端缺少更新清单验证公钥。");
        return resources.Select(name =>
        {
            using Stream stream = assembly.GetManifestResourceStream(name)
                ?? throw new InvalidOperationException($"无法读取更新清单验证公钥：{name}");
            using var reader = new StreamReader(stream, Encoding.UTF8, true, 4096, false);
            return reader.ReadToEnd();
        }).ToArray();
    }

    private sealed record ReleaseCandidate(
        GitHubReleaseDto Release,
        SemanticVersion Version,
        Uri ReleasePage);

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

    private sealed record SignedUpdateManifestDto(
        [property: JsonPropertyName("schemaVersion")] int SchemaVersion,
        [property: JsonPropertyName("product")] string Product,
        [property: JsonPropertyName("version")] string Version,
        [property: JsonPropertyName("channel")] string Channel,
        [property: JsonPropertyName("publishedAtUtc")] DateTimeOffset PublishedAtUtc,
        [property: JsonPropertyName("releasePageUrl")] string ReleasePageUrl,
        [property: JsonPropertyName("installer")] SignedInstallerDto Installer);

    private sealed record SignedInstallerDto(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("size")] long Size,
        [property: JsonPropertyName("sha256")] string Sha256,
        [property: JsonPropertyName("publisherCertificateSha256")] string PublisherCertificateSha256);
}
