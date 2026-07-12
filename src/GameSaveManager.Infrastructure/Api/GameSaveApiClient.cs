using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using GameSaveManager.Application.Api;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.Infrastructure.Api;

/// <summary>基于 HttpClient 的 GameSave 服务端 API 实现。</summary>
public sealed class GameSaveApiClient(HttpClient httpClient) : IGameSaveApiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public Task<AuthSession> RegisterAsync(
        Uri server,
        string username,
        string password,
        string deviceId,
        string deviceName,
        CancellationToken cancellationToken) =>
        PostJsonAsync<AuthSession>(server, "api/game-save/v1/auth/register", null, new
        {
            username,
            password,
            deviceId,
            deviceName
        }, cancellationToken);

    public Task<AuthSession> LoginAsync(
        Uri server,
        string username,
        string password,
        string deviceId,
        string deviceName,
        CancellationToken cancellationToken) =>
        PostJsonAsync<AuthSession>(server, "api/game-save/v1/auth/login", null, new
        {
            username,
            password,
            deviceId,
            deviceName
        }, cancellationToken);

    public async Task<IReadOnlyList<CloudGame>> ListGamesAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Get, server, "api/game-save/v1/games", deviceToken);
        return await SendForDataAsync<List<CloudGame>>(request, cancellationToken);
    }

    public async Task<CloudQuota> GetQuotaAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Get, server, "api/game-save/v1/account/quota", deviceToken);
        return await SendForDataAsync<CloudQuota>(request, cancellationToken);
    }
    public async Task<IReadOnlyList<CloudDevice>> ListDevicesAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Get, server, "api/game-save/v1/devices", deviceToken);
        return await SendForDataAsync<List<CloudDevice>>(request, cancellationToken);
    }

    public async Task RevokeDeviceAsync(
        Uri server,
        string deviceToken,
        string deviceId,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/devices/{Uri.EscapeDataString(deviceId)}";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Delete, server, path, deviceToken);
        await SendForSuccessAsync(request, cancellationToken);
    }
    public Task<CloudGame> CreateGameAsync(
        Uri server,
        string deviceToken,
        string name,
        string provider,
        string? providerGameId,
        CancellationToken cancellationToken) =>
        PostJsonAsync<CloudGame>(server, "api/game-save/v1/games", deviceToken, new
        {
            name,
            provider,
            providerGameId
        }, cancellationToken);

    public async Task<CloudHead> GetHeadAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/head";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, server, path, deviceToken);
        return await SendForDataAsync<CloudHead>(request, cancellationToken);
    }

    public async Task<IReadOnlyList<CloudSnapshotSummary>> ListSnapshotsAsync(
        Uri server,
        string deviceToken,
        string gameId,
        int limit,
        CancellationToken cancellationToken)
    {
        int safeLimit = Math.Clamp(limit, 1, 200);
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/snapshots?limit={safeLimit}";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, server, path, deviceToken);
        return await SendForDataAsync<List<CloudSnapshotSummary>>(request, cancellationToken);
    }

    public async Task DeleteSnapshotAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/snapshots/{Uri.EscapeDataString(snapshotId)}";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Delete, server, path, deviceToken);
        await SendForSuccessAsync(request, cancellationToken);
    }

    public async Task<IReadOnlyList<ContentObjectDescriptor>> CheckMissingAsync(
        Uri server,
        string deviceToken,
        IReadOnlyCollection<ContentObjectDescriptor> objects,
        CancellationToken cancellationToken)
    {
        return await PostJsonAsync<List<ContentObjectDescriptor>>(
            server,
            "api/game-save/v1/objects/check",
            deviceToken,
            new { objects },
            cancellationToken);
    }

    public async Task UploadObjectAsync(
        Uri server,
        string deviceToken,
        string filePath,
        ContentObjectDescriptor descriptor,
        CancellationToken cancellationToken)
    {
        await using FileStream stream = new(
            filePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var multipart = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream, 1024 * 1024);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", Path.GetFileName(filePath));
        multipart.Add(new StringContent(descriptor.Sha256, Encoding.UTF8), "sha256");
        multipart.Add(new StringContent(descriptor.Size.ToString(CultureInfo.InvariantCulture), Encoding.UTF8), "size");

        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post, server, "api/game-save/v1/objects", deviceToken);
        request.Content = multipart;
        await SendForDataAsync<GameObjectResponse>(request, cancellationToken);
    }

    public Task<CloudSnapshotResult> CommitSnapshotAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string? expectedHeadSnapshotId,
        SnapshotTrigger trigger,
        string? description,
        IReadOnlyList<SnapshotFile> files,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/snapshots";
        return PostJsonAsync<CloudSnapshotResult>(server, path, deviceToken, new
        {
            expectedHeadSnapshotId,
            triggerType = SnapshotTriggerNames.ToApiValue(trigger),
            description,
            files = files.Select(file => new
            {
                path = file.RelativePath,
                sha256 = file.Sha256,
                size = file.Size
            })
        }, cancellationToken);
    }

    public async Task<CloudSnapshotManifest> GetSnapshotAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string snapshotId,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/snapshots/{Uri.EscapeDataString(snapshotId)}";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, server, path, deviceToken);
        return await SendForDataAsync<CloudSnapshotManifest>(request, cancellationToken);
    }

    public async Task DownloadObjectAsync(
        Uri server,
        string deviceToken,
        string objectId,
        string destinationPath,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/objects/{Uri.EscapeDataString(objectId)}/download-url";
        using HttpRequestMessage authorizationRequest = CreateRequest(HttpMethod.Get, server, path, deviceToken);
        string downloadUrl = await SendForDataAsync<string>(authorizationRequest, cancellationToken);
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? downloadUri))
        {
            throw new InvalidDataException("服务端返回的对象下载地址不合法");
        }

        // 预签名地址已携带访问授权；禁止把设备 Token 转发给对象存储服务。
        using HttpRequestMessage request = new(HttpMethod.Get, downloadUri);
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new GameSaveApiException(
                (int)response.StatusCode,
                "OBJECT_DOWNLOAD_FAILED",
                $"对象下载请求失败: {(int)response.StatusCode}");
        }

        string? parent = Path.GetDirectoryName(destinationPath);
        if (string.IsNullOrWhiteSpace(parent))
        {
            throw new ArgumentException("下载目标必须位于一个目录中", nameof(destinationPath));
        }
        Directory.CreateDirectory(parent);
        await using Stream input = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using FileStream output = new(
            destinationPath,
            FileMode.CreateNew,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        await input.CopyToAsync(output, 1024 * 1024, cancellationToken);
        await output.FlushAsync(cancellationToken);
    }

    private async Task<T> PostJsonAsync<T>(
        Uri server,
        string path,
        string? deviceToken,
        object body,
        CancellationToken cancellationToken)
        where T : class
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, server, path, deviceToken);
        request.Content = JsonContent.Create(body, options: JsonOptions);
        return await SendForDataAsync<T>(request, cancellationToken);
    }

    /// <summary>处理不返回 data 的成功响应，同时保留统一错误码解析。</summary>
    private async Task SendForSuccessAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        if (response.IsSuccessStatusCode) return;

        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiEnvelope<object>? envelope = string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ApiEnvelope<object>>(json, JsonOptions);
        throw new GameSaveApiException(
            (int)response.StatusCode,
            envelope?.Code ?? "HTTP_ERROR",
            envelope?.Msg ?? $"GameSave API request failed: {(int)response.StatusCode}");
    }
    private async Task<T> SendForDataAsync<T>(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
        where T : class
    {
        using HttpResponseMessage response = await httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        string json = await response.Content.ReadAsStringAsync(cancellationToken);
        ApiEnvelope<T>? envelope = string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<ApiEnvelope<T>>(json, JsonOptions);
        if (!response.IsSuccessStatusCode)
        {
            throw new GameSaveApiException(
                (int)response.StatusCode,
                envelope?.Code ?? "HTTP_ERROR",
                envelope?.Msg ?? $"GameSave API 请求失败: {(int)response.StatusCode}");
        }
        if (envelope?.Data is null)
        {
            throw new InvalidDataException("GameSave API 成功响应缺少 data 字段");
        }
        return envelope.Data;
    }

    private static HttpRequestMessage CreateRequest(
        HttpMethod method,
        Uri server,
        string path,
        string? deviceToken)
    {
        Uri baseUri = new(server.AbsoluteUri.TrimEnd('/') + "/", UriKind.Absolute);
        var request = new HttpRequestMessage(method, new Uri(baseUri, path));
        if (!string.IsNullOrWhiteSpace(deviceToken))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", deviceToken);
        }
        return request;
    }

    private sealed class ApiEnvelope<T>
        where T : class
    {
        public int Status { get; set; }
        public string? Code { get; set; }
        public string? Msg { get; set; }
        public T? Data { get; set; }
    }

    private sealed class GameObjectResponse
    {
        public string ObjectId { get; set; } = string.Empty;
        public string Sha256 { get; set; } = string.Empty;
        public long Size { get; set; }
    }
}