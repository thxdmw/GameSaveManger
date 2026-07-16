namespace GameSaveManager.Application.Api;

public enum ErrorCategory
{
    Authentication,
    Authorization,
    RateLimited,
    Timeout,
    Network,
    Server,
    Conflict,
    Validation,
    Cancelled,
    Unknown
}

/// <summary>供界面展示的统一客户端错误；不泄漏凭据、地址或底层异常细节。</summary>
public sealed record ClientOperationError(
    ErrorCategory Category,
    string UserMessage,
    bool CanRetry,
    TimeSpan? SuggestedRetryDelay,
    string? RequestId)
{
    public static ClientOperationError FromException(Exception exception) => exception switch
    {
        GameSaveApiException api => FromApiException(api),
        TaskCanceledException => new(ErrorCategory.Timeout, "请求超时，请检查网络连接。", true, null, null),
        HttpRequestException => new(ErrorCategory.Network, "无法连接到服务端，请检查网络、DNS 和服务端地址。", true, null, null),
        OperationCanceledException => new(ErrorCategory.Cancelled, "操作已取消。", false, null, null),
        InvalidDataException => new(ErrorCategory.Validation, "服务端返回了无法识别的数据。", true, null, null),
        _ => new(ErrorCategory.Unknown, exception.Message, false, null, null)
    };

    private static ClientOperationError FromApiException(GameSaveApiException exception) => exception.StatusCode switch
    {
        401 => new(ErrorCategory.Authentication, "登录状态已失效，请重新登录。", false, null, exception.RequestId),
        403 => new(ErrorCategory.Authorization, "当前账号没有执行此操作的权限。", false, null, exception.RequestId),
        409 => new(ErrorCategory.Conflict, exception.Message, false, null, exception.RequestId),
        429 => new(ErrorCategory.RateLimited, "请求过于频繁，请稍后重试。", true, exception.SuggestedRetryDelay, exception.RequestId),
        >= 500 => new(ErrorCategory.Server, "服务端暂时不可用。", true, exception.SuggestedRetryDelay, exception.RequestId),
        >= 400 and < 500 => new(ErrorCategory.Validation, exception.Message, false, null, exception.RequestId),
        _ => new(ErrorCategory.Unknown, exception.Message, false, null, exception.RequestId)
    };
}
