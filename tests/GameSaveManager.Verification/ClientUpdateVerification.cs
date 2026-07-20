using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GameSaveManager.Application.Updates;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Updates;

namespace GameSaveManager.Verification;

internal static class ClientUpdateVerification
{
    private const string Version = "0.2.0";
    private const string ReleasePage = "https://github.com/thxdmw/GameSaveManger/releases/tag/v0.2.0";
    private const string InstallerName = "GameSaveManager-Setup-0.2.0.exe";
    private const string InstallerUrl = "https://github.com/thxdmw/GameSaveManger/releases/download/v0.2.0/GameSaveManager-Setup-0.2.0.exe";
    private const string PublisherCertificateSha256 = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";

    public static async Task VerifyAsync()
    {
        VerifySemanticVersionOrdering();
        string tempDirectory = Path.Combine(
            Path.GetTempPath(),
            "GameSaveManager.Verification",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDirectory);
        try
        {
            byte[] installerBytes = Encoding.UTF8.GetBytes("verified installer payload");
            string installerHash = Hash(installerBytes);
            byte[] checksumBytes = Encoding.UTF8.GetBytes($"{installerHash}  {InstallerName}\n");

            using ECDsa signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            string publicKeyPem = signingKey.ExportSubjectPublicKeyInfoPem();
            byte[] manifestBytes = JsonSerializer.SerializeToUtf8Bytes(new
            {
                schemaVersion = 1,
                product = "GameSaveManager",
                version = Version,
                channel = "preview",
                publishedAtUtc = "2026-07-17T00:00:00Z",
                releasePageUrl = ReleasePage,
                installer = new
                {
                    name = InstallerName,
                    url = InstallerUrl,
                    size = installerBytes.Length,
                    sha256 = installerHash,
                    publisherCertificateSha256 = PublisherCertificateSha256
                }
            });
            byte[] signatureBytes = Encoding.ASCII.GetBytes(Convert.ToBase64String(signingKey.SignData(
                manifestBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation)));
            string releasesJson = CreateReleaseJson(installerBytes, checksumBytes, manifestBytes, signatureBytes);

            using var httpClient = new HttpClient(new StaticUpdateHandler(
                releasesJson,
                installerBytes,
                checksumBytes,
                manifestBytes,
                signatureBytes));
            var service = new GitHubClientUpdateService(
                httpClient,
                Path.Combine(tempDirectory, "updates"),
                new FixedAuthenticodeVerifier(PublisherCertificateSha256),
                publicKeyPem);
            ClientUpdateRelease? preview = await service.CheckForUpdateAsync("0.1.0", true, CancellationToken.None);
            Check(preview is { Version: Version, Prerelease: true }, "预发布通道未发现有效更新");
            Check(preview!.PublisherCertificateSha256 == PublisherCertificateSha256, "签名清单中的发布者证书未固定");
            ClientUpdateRelease? stable = await service.CheckForUpdateAsync("0.1.0", false, CancellationToken.None);
            Check(stable is null, "稳定通道不应接收预发布更新");

            PreparedClientUpdate prepared = await service.DownloadUpdateAsync(
                preview,
                "0.1.0",
                null,
                CancellationToken.None);
            Check(File.Exists(prepared.InstallerPath), "校验通过的安装包未写入受控目录");
            Check(prepared.PreviousVersion == "0.1.0", "更新事务未记录上一版本");
            Check(string.Equals(prepared.VerifiedSha256, installerHash, StringComparison.Ordinal), "安装包摘要记录错误");

            ClientUpdateRelease tampered = preview with
            {
                Installer = preview.Installer with { Sha256 = new string('0', 64) }
            };
            await ExpectThrowsAsync<InvalidDataException>(() =>
                service.DownloadUpdateAsync(tampered, "0.1.0", null, CancellationToken.None));

            var wrongPublisherService = new GitHubClientUpdateService(
                httpClient,
                Path.Combine(tempDirectory, "publisher-mismatch"),
                new FixedAuthenticodeVerifier(new string('b', 64)),
                publicKeyPem);
            ClientUpdateRelease? wrongPublisherRelease = await wrongPublisherService.CheckForUpdateAsync(
                "0.1.0",
                true,
                CancellationToken.None);
            await ExpectThrowsAsync<CryptographicException>(() => wrongPublisherService.DownloadUpdateAsync(
                wrongPublisherRelease!,
                "0.1.0",
                null,
                CancellationToken.None));

            using ECDsa unrelatedKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            var invalidManifestService = new GitHubClientUpdateService(
                httpClient,
                Path.Combine(tempDirectory, "invalid-manifest"),
                new FixedAuthenticodeVerifier(PublisherCertificateSha256),
                unrelatedKey.ExportSubjectPublicKeyInfoPem());
            await ExpectThrowsAsync<CryptographicException>(() => invalidManifestService.CheckForUpdateAsync(
                "0.1.0",
                true,
                CancellationToken.None));

            string unsignedFile = Path.Combine(tempDirectory, "unsigned.exe");
            await File.WriteAllBytesAsync(unsignedFile, installerBytes);
            AuthenticodeVerificationResult unsignedResult = new WindowsAuthenticodeVerifier().Verify(unsignedFile);
            Check(!unsignedResult.Trusted, "未签名文件不应通过 Windows 发布者验证");

            string databasePath = Path.Combine(tempDirectory, "preferences.db");
            var database = new SqliteDatabase(databasePath);
            await database.InitializeAsync(CancellationToken.None);
            var preferenceStore = new SqliteUpdatePreferenceStore(database);
            var saved = new ClientUpdatePreferences(false, DateTimeOffset.Parse("2026-07-17T00:00:00Z"), Version);
            await preferenceStore.SaveAsync(saved, CancellationToken.None);
            ClientUpdatePreferences loaded = await preferenceStore.LoadAsync(CancellationToken.None);
            Check(loaded == saved, "更新检查偏好未正确持久化");
        }
        finally
        {
            try { Directory.Delete(tempDirectory, true); } catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string CreateReleaseJson(
        byte[] installerBytes,
        byte[] checksumBytes,
        byte[] manifestBytes,
        byte[] signatureBytes) =>
        JsonSerializer.Serialize(new[]
        {
            new
            {
                tag_name = "v0.2.0",
                html_url = ReleasePage,
                body = "更新验证说明",
                draft = false,
                prerelease = true,
                published_at = "2026-07-17T00:00:00Z",
                assets = new object[]
                {
                    Asset(InstallerName, installerBytes, InstallerUrl),
                    Asset("SHA256SUMS.txt", checksumBytes, "https://github.com/thxdmw/GameSaveManger/releases/download/v0.2.0/SHA256SUMS.txt"),
                    Asset("update-manifest.json", manifestBytes, "https://github.com/thxdmw/GameSaveManger/releases/download/v0.2.0/update-manifest.json"),
                    Asset("update-manifest.json.sig", signatureBytes, "https://github.com/thxdmw/GameSaveManger/releases/download/v0.2.0/update-manifest.json.sig")
                }
            }
        });

    private static object Asset(string name, byte[] bytes, string url) => new
    {
        name,
        state = "uploaded",
        size = bytes.Length,
        digest = $"sha256:{Hash(bytes)}",
        browser_download_url = url
    };

    private static void VerifySemanticVersionOrdering()
    {
        Check(SemanticVersion.Parse("0.2.0").CompareTo(SemanticVersion.Parse("0.1.9")) > 0, "次版本比较错误");
        Check(SemanticVersion.Parse("0.2.0").CompareTo(SemanticVersion.Parse("0.2.0-preview.2")) > 0, "正式版本应高于同版本预览版");
        Check(SemanticVersion.Parse("0.2.0-preview.10").CompareTo(SemanticVersion.Parse("0.2.0-preview.2")) > 0, "预览版数字标识比较错误");
        Check(!SemanticVersion.TryParse("0.2", out _), "不完整版本号不应通过");
    }

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action)
        where TException : Exception
    {
        try
        {
            await action();
        }
        catch (TException)
        {
            return;
        }
        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}");
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class FixedAuthenticodeVerifier(string publisherCertificateSha256) : IAuthenticodeVerifier
    {
        public AuthenticodeVerificationResult Verify(string filePath) =>
            File.Exists(filePath)
                ? new(true, publisherCertificateSha256, null)
                : new(false, null, "文件不存在");
    }

    private sealed class StaticUpdateHandler(
        string releasesJson,
        byte[] installerBytes,
        byte[] checksumBytes,
        byte[] manifestBytes,
        byte[] signatureBytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            string path = request.RequestUri?.AbsolutePath ?? string.Empty;
            HttpContent content = path.EndsWith("/releases", StringComparison.Ordinal)
                ? new StringContent(releasesJson, Encoding.UTF8, "application/json")
                : path.EndsWith("/SHA256SUMS.txt", StringComparison.Ordinal)
                    ? new ByteArrayContent(checksumBytes)
                    : path.EndsWith("/update-manifest.json.sig", StringComparison.Ordinal)
                        ? new ByteArrayContent(signatureBytes)
                        : path.EndsWith("/update-manifest.json", StringComparison.Ordinal)
                            ? new ByteArrayContent(manifestBytes)
                            : path.EndsWith($"/{InstallerName}", StringComparison.Ordinal)
                                ? new ByteArrayContent(installerBytes)
                                : throw new InvalidOperationException($"未预期的更新请求：{request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
