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
    private const long MaximumJsonResponseBytes = 32L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new CmsDateTimeOffsetConverter());
        return options;
    }

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

    public async Task<CloudAccountSession> GetSessionAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Get, server, "api/game-save/v1/account/session", deviceToken);
        return await SendForDataAsync<CloudAccountSession>(request, cancellationToken);
    }

    public async Task<IReadOnlyList<CloudGame>> ListGamesAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Get, server, "api/game-save/v1/games", deviceToken);
        List<CloudGame> games = await SendForDataAsync<List<CloudGame>>(request, cancellationToken);
        CloudApiResponseValidator.ValidateGames(games);
        return games;
    }

    public async Task<CloudRetentionPolicy> GetRetentionPolicyAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/retention";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, server, path, deviceToken);
        CloudRetentionPolicy policy = await SendForDataAsync<CloudRetentionPolicy>(request, cancellationToken);
        CloudApiResponseValidator.ValidateRetentionPolicy(policy, gameId);
        return policy;
    }

    public async Task<CloudRetentionPolicy> UpdateRetentionPolicyAsync(
        Uri server,
        string deviceToken,
        string gameId,
        bool enabled,
        int retentionCount,
        int retentionDays,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/retention";
        CloudRetentionPolicy policy = await PutJsonAsync<CloudRetentionPolicy>(server, path, deviceToken, new
        {
            enabled,
            retentionCount,
            retentionDays
        }, cancellationToken);
        CloudApiResponseValidator.ValidateRetentionPolicy(policy, gameId);
        return policy;
    }

    public async Task<CloudRetentionCleanupResult> CleanupRetentionAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/retention/cleanup";
        CloudRetentionCleanupResult result = await PostJsonAsync<CloudRetentionCleanupResult>(
            server, path, deviceToken, new { }, cancellationToken);
        CloudApiResponseValidator.ValidateRetentionCleanup(result, gameId);
        return result;
    }
    public async Task<CloudQuota> GetQuotaAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Get, server, "api/game-save/v1/account/quota", deviceToken);
        CloudQuota quota = await SendForDataAsync<CloudQuota>(request, cancellationToken);
        CloudApiResponseValidator.ValidateQuota(quota);
        return quota;
    }
    public async Task<IReadOnlyList<CloudDevice>> ListDevicesAsync(
        Uri server,
        string deviceToken,
        CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Get, server, "api/game-save/v1/devices", deviceToken);
        List<CloudDevice> devices = await SendForDataAsync<List<CloudDevice>>(request, cancellationToken);
        CloudApiResponseValidator.ValidateDevices(devices);
        return devices;
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
    public async Task<CloudGame> CreateGameAsync(
        Uri server,
        string deviceToken,
        string name,
        string provider,
        string? providerGameId,
        CancellationToken cancellationToken)
    {
        CloudGame game = await PostJsonAsync<CloudGame>(server, "api/game-save/v1/games", deviceToken, new
        {
            name,
            provider,
            providerGameId
        }, cancellationToken);
        CloudApiResponseValidator.ValidateCreatedGame(game, name, provider);
        return game;
    }

    public async Task DeleteGameAsync(
        Uri server,
        string deviceToken,
        string gameId,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}";
        using HttpRequestMessage request = CreateRequest(HttpMethod.Delete, server, path, deviceToken);
        await SendForSuccessAsync(request, cancellationToken);
    }

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
        List<CloudSnapshotSummary> snapshots = await SendForDataAsync<List<CloudSnapshotSummary>>(request, cancellationToken);
        CloudApiResponseValidator.ValidateSnapshots(snapshots, safeLimit);
        return snapshots;
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
            FileShare.Read,
            bufferSize: 1024 * 1024,
            options: FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length != descriptor.Size)
            throw new IOException($"待上传文件在扫描后发生变化：{Path.GetFileName(filePath)}");
        using var multipart = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream, 1024 * 1024);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        multipart.Add(fileContent, "file", Path.GetFileName(filePath));
        multipart.Add(new StringContent(descriptor.Sha256, Encoding.UTF8), "sha256");
        multipart.Add(new StringContent(descriptor.Size.ToString(CultureInfo.InvariantCulture), Encoding.UTF8), "size");

        using HttpRequestMessage request = CreateRequest(
            HttpMethod.Post, server, "api/game-save/v1/objects", deviceToken);
        request.Content = multipart;
        GameObjectResponse uploaded = await SendForDataAsync<GameObjectResponse>(request, cancellationToken);
        if (string.IsNullOrWhiteSpace(uploaded.ObjectId)
            || uploaded.Size != descriptor.Size
            || !string.Equals(uploaded.Sha256, descriptor.Sha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("服务端返回的上传对象描述与本机内容不一致，已停止提交快照。");
    }

    public Task<CloudSnapshotResult> CommitSnapshotAsync(
        Uri server,
        string deviceToken,
        string gameId,
        string? expectedHeadSnapshotId,
        SnapshotTrigger trigger,
        string? description,
        IReadOnlyList<SnapshotRootDescriptor> roots,
        IReadOnlyList<SnapshotFile> files,
        CancellationToken cancellationToken)
    {
        string path = $"api/game-save/v1/games/{Uri.EscapeDataString(gameId)}/snapshots";
        return PostJsonAsync<CloudSnapshotResult>(server, path, deviceToken, new
        {
            expectedHeadSnapshotId,
            triggerType = SnapshotTriggerNames.ToApiValue(trigger),
            description,
            roots,
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
        CloudSnapshotManifest manifest = await SendForDataAsync<CloudSnapshotManifest>(request, cancellationToken);
        CloudApiResponseValidator.ValidateManifest(manifest, gameId, snapshotId);
        return manifest;
    }

    public async Task DownloadObjectAsync(
        Uri server,
        string deviceToken,
        string objectId,
        string destinationPath,
        long expectedSize,
        CancellationToken cancellationToken)
    {
        if (expectedSize < 0) throw new ArgumentOutOfRangeException(nameof(expectedSize));
        string path = $"api/game-save/v1/objects/{Uri.EscapeDataString(objectId)}/download-url";
        using HttpRequestMessage authorizationRequest = CreateRequest(HttpMethod.Get, server, path, deviceToken);
        string downloadUrl = await SendForDataAsync<string>(authorizationRequest, cancellationToken);
        if (!Uri.TryCreate(downloadUrl, UriKind.Absolute, out Uri? downloadUri))
        {
            throw new InvalidDataException("服务端返回的对象下载地址不合法");
        }
        if (!string.Equals(downloadUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            && !(downloadUri.IsLoopback && string.Equals(downloadUri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("对象下载地址必须使用 HTTPS；仅本机回环开发地址允许 HTTP。");

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
                $"对象下载请求失败: {(int)response.StatusCode}", GetRetryDelay(response), GetRequestId(response));
        }
        if (response.Content.Headers.ContentLength is long contentLength && contentLength > expectedSize)
            throw new InvalidDataException($"对象下载响应超过预期大小：{contentLength} > {expectedSize}");

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
        byte[] buffer = new byte[1024 * 1024];
        long total = 0;
        while (true)
        {
            int read = await input.ReadAsync(buffer, cancellationToken);
            if (read == 0) break;
            total = checked(total + read);
            if (total > expectedSize)
                throw new InvalidDataException($"对象下载内容超过预期大小：{total} > {expectedSize}");
            await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }
        if (total != expectedSize)
            throw new InvalidDataException($"对象下载大小不完整：{total} != {expectedSize}");
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

    private async Task<T> PutJsonAsync<T>(
        Uri server,
        string path,
        string? deviceToken,
        object body,
        CancellationToken cancellationToken)
        where T : class
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Put, server, path, deviceToken);
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

        string json = await ReadBoundedJsonAsync(response.Content, cancellationToken);
        ApiEnvelope<object>? envelope = DeserializeEnvelope<object>(json);
        throw new GameSaveApiException(
            (int)response.StatusCode,
            envelope?.Code ?? "HTTP_ERROR",
            envelope?.Msg ?? $"GameSave API request failed: {(int)response.StatusCode}", GetRetryDelay(response), GetRequestId(response));
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
        string json = await ReadBoundedJsonAsync(response.Content, cancellationToken);
        ApiEnvelope<T>? envelope = DeserializeEnvelope<T>(json);
        if (!response.IsSuccessStatusCode)
        {
            throw new GameSaveApiException(
                (int)response.StatusCode,
                envelope?.Code ?? "HTTP_ERROR",
                envelope?.Msg ?? $"GameSave API 请求失败: {(int)response.StatusCode}", GetRetryDelay(response), GetRequestId(response));
        }
        if (envelope?.Data is null)
        {
            throw new InvalidDataException("GameSave API 成功响应缺少 data 字段");
        }
        return envelope.Data;
    }

    private static async Task<string> ReadBoundedJsonAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long contentLength
            && contentLength > MaximumJsonResponseBytes)
            throw new InvalidDataException("GameSave API 响应超过客户端允许的大小上限。");
        try
        {
            await content.LoadIntoBufferAsync(MaximumJsonResponseBytes, cancellationToken);
            return await content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            throw new InvalidDataException("GameSave API 响应超过客户端允许的大小或无法完整读取。", exception);
        }
    }

    private static ApiEnvelope<T>? DeserializeEnvelope<T>(string json)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try { return JsonSerializer.Deserialize<ApiEnvelope<T>>(json, JsonOptions); }
        catch (JsonException exception)
        {
            throw new InvalidDataException("GameSave API 返回了无法识别的 JSON 数据。", exception);
        }
    }

    private static TimeSpan? GetRetryDelay(HttpResponseMessage response)
    {
        if (response.Headers.RetryAfter?.Delta is { } delta) return delta;
        if (response.Headers.RetryAfter?.Date is { } date)
        {
            TimeSpan delay = date - DateTimeOffset.UtcNow;
            return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
        }
        return null;
    }

    private static string? GetRequestId(HttpResponseMessage response)
    {
        foreach (string header in new[] { "X-Request-ID", "Request-ID" })
        {
            if (response.Headers.TryGetValues(header, out IEnumerable<string>? values))
                return values.FirstOrDefault()?.Trim() is { Length: > 0 } value ? value[..Math.Min(value.Length, 128)] : null;
        }
        return null;
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
