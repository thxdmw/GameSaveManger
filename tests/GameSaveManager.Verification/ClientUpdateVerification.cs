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
            string checksumText = $"{installerHash}  GameSaveManager-Setup-0.2.0.exe\n";
            byte[] checksumBytes = Encoding.UTF8.GetBytes(checksumText);
            string checksumHash = Hash(checksumBytes);
            string releasesJson = JsonSerializer.Serialize(new[]
            {
                new
                {
                    tag_name = "v0.2.0",
                    html_url = "https://github.com/thxdmw/GameSaveManger/releases/tag/v0.2.0",
                    body = "更新验证说明",
                    draft = false,
                    prerelease = true,
                    published_at = "2026-07-17T00:00:00Z",
                    assets = new object[]
                    {
                        new
                        {
                            name = "GameSaveManager-Setup-0.2.0.exe",
                            state = "uploaded",
                            size = installerBytes.Length,
                            digest = $"sha256:{installerHash}",
                            browser_download_url = "https://github.com/thxdmw/GameSaveManger/releases/download/v0.2.0/GameSaveManager-Setup-0.2.0.exe"
                        },
                        new
                        {
                            name = "SHA256SUMS.txt",
                            state = "uploaded",
                            size = checksumBytes.Length,
                            digest = $"sha256:{checksumHash}",
                            browser_download_url = "https://github.com/thxdmw/GameSaveManger/releases/download/v0.2.0/SHA256SUMS.txt"
                        }
                    }
                }
            });

            using var httpClient = new HttpClient(new StaticUpdateHandler(releasesJson, installerBytes, checksumBytes));
            var service = new GitHubClientUpdateService(httpClient, Path.Combine(tempDirectory, "updates"));
            ClientUpdateRelease? preview = await service.CheckForUpdateAsync("0.1.0", true, CancellationToken.None);
            Check(preview is { Version: "0.2.0", Prerelease: true }, "预发布通道未发现有效更新");
            ClientUpdateRelease? stable = await service.CheckForUpdateAsync("0.1.0", false, CancellationToken.None);
            Check(stable is null, "稳定通道不应接收预发布更新");

            PreparedClientUpdate prepared = await service.DownloadUpdateAsync(preview!, null, CancellationToken.None);
            Check(File.Exists(prepared.InstallerPath), "校验通过的安装包未写入受控目录");
            Check(string.Equals(prepared.VerifiedSha256, installerHash, StringComparison.Ordinal), "安装包摘要记录错误");

            ClientUpdateRelease tampered = preview! with
            {
                Installer = preview.Installer with { Sha256 = new string('0', 64) }
            };
            await ExpectThrowsAsync<InvalidDataException>(() =>
                service.DownloadUpdateAsync(tampered, null, CancellationToken.None));

            string databasePath = Path.Combine(tempDirectory, "preferences.db");
            var database = new SqliteDatabase(databasePath);
            await database.InitializeAsync(CancellationToken.None);
            var preferenceStore = new SqliteUpdatePreferenceStore(database);
            var saved = new ClientUpdatePreferences(false, DateTimeOffset.Parse("2026-07-17T00:00:00Z"), "0.2.0");
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

    private sealed class StaticUpdateHandler(
        string releasesJson,
        byte[] installerBytes,
        byte[] checksumBytes) : HttpMessageHandler
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
                    : path.EndsWith("/GameSaveManager-Setup-0.2.0.exe", StringComparison.Ordinal)
                        ? new ByteArrayContent(installerBytes)
                        : throw new InvalidOperationException($"未预期的更新请求：{request.RequestUri}");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        }
    }
}
