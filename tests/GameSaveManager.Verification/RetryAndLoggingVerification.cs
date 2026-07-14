using System.Net;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.Diagnostics;

internal static class RetryAndLoggingVerification
{
    public static async Task VerifySafeRetryHandlerAsync()
    {
        var getHandler = new CountingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var getClient = new HttpClient(new SafeRetryHandler(getHandler));
        using HttpResponseMessage getResponse = await getClient.GetAsync("https://example.test/health");
        Ensure(getResponse.StatusCode == HttpStatusCode.OK, "GET 应在短暂故障后成功返回。");
        Ensure(getHandler.CallCount == 2, "GET 应只额外重试一次。");

        var postHandler = new CountingHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var postClient = new HttpClient(new SafeRetryHandler(postHandler));
        using HttpResponseMessage postResponse = await postClient.PostAsync(
            "https://example.test/objects", new StringContent("payload"));
        Ensure(postResponse.StatusCode == HttpStatusCode.ServiceUnavailable, "写请求不应被重试。");
        Ensure(postHandler.CallCount == 1, "POST 只能发送一次。");
    }

    public static async Task VerifyJsonFileLoggerAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
        try
        {
            var logger = new JsonFileLogger(directory);
            logger.Error(
                "verification.log",
                CreateLoggedException(),
                "Bearer device-token password=plain-text");
            string path = Directory.EnumerateFiles(directory, "gamesave-*.jsonl").Single();
            string content = await File.ReadAllTextAsync(path);
            Ensure(!content.Contains("device-token", StringComparison.Ordinal), "日志不得写入 Bearer Token。");
            Ensure(!content.Contains("plain-text", StringComparison.Ordinal), "日志不得写入密码值。");
            Ensure(content.Contains("Bearer ***", StringComparison.Ordinal), "日志应保留脱敏后的 Bearer 标记。");
            Ensure(!content.Contains("plain-token", StringComparison.Ordinal), "Device token must be sanitized.");
            Ensure(content.Contains("InvalidOperationException", StringComparison.Ordinal), "Exception type must be recorded.");
            Ensure(content.Contains("ArgumentException", StringComparison.Ordinal), "Inner exception details must be recorded.");
            Ensure(content.Contains("RetryAndLoggingVerification", StringComparison.Ordinal), "Exception stack trace must be recorded.");
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static Exception CreateLoggedException()
    {
        try
        {
            throw new ArgumentException("deviceToken=plain-token");
        }
        catch (Exception innerException)
        {
            try
            {
                throw new InvalidOperationException("password=plain-text", innerException);
            }
            catch (Exception exception)
            {
                return exception;
            }
        }
    }
    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private sealed class CountingHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _statuses = new(statuses);

        public int CallCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            HttpStatusCode status = _statuses.Count > 0 ? _statuses.Dequeue() : HttpStatusCode.OK;
            return Task.FromResult(new HttpResponseMessage(status));
        }
    }
}