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

    public async Task<IReadOnlyList<ContentObjectDescriptor>> CheckMissingAsync(
        Uri server,
        string deviceToken,
        IReadOnlyCollection<ContentObjectDescriptor> objects,
        CancellationToken cancellationToken)
    {
        // System.Text.Json 不直接实例化 IReadOnlyList<T> 接口，先反序列化为具体 List<T>。
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
