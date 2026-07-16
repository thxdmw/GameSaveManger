using GameSaveManager.Application.Api;

namespace GameSaveManager.Verification;

internal static class ClientOperationErrorVerification
{
    public static void VerifyClassification()
    {
        ClientOperationError authentication = ClientOperationError.FromException(
            new GameSaveApiException(401, "TOKEN_EXPIRED", "raw server text", requestId: "request-auth"));
        Ensure(authentication.Category == ErrorCategory.Authentication, "401 应归类为认证失效。");
        Ensure(!authentication.CanRetry, "认证失效不应提示盲目重试。");
        Ensure(authentication.UserMessage.Contains("重新登录", StringComparison.Ordinal), "认证失效应提示重新登录。");

        TimeSpan retryAfter = TimeSpan.FromSeconds(3);
        ClientOperationError rateLimited = ClientOperationError.FromException(
            new GameSaveApiException(429, "RATE_LIMITED", "raw server text", retryAfter, "request-rate"));
        Ensure(rateLimited.Category == ErrorCategory.RateLimited, "429 应归类为限流。");
        Ensure(rateLimited.CanRetry && rateLimited.SuggestedRetryDelay == retryAfter, "429 应保留 Retry-After。");
        Ensure(rateLimited.RequestId == "request-rate", "统一错误应保留请求 ID。");

        ClientOperationError network = ClientOperationError.FromException(new HttpRequestException("dns"));
        Ensure(network.Category == ErrorCategory.Network && network.CanRetry, "断网或 DNS 失败应提示可重试。");

        ClientOperationError timeout = ClientOperationError.FromException(new TaskCanceledException("timeout"));
        Ensure(timeout.Category == ErrorCategory.Timeout && timeout.CanRetry, "请求超时应提示可重试。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
