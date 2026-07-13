using System.Net;

namespace GameSaveManager.Infrastructure.Api;

/// <summary>仅对安全 GET 请求执行有限瞬时故障重试；任何写操作都只发送一次。</summary>
public sealed class SafeRetryHandler(HttpMessageHandler innerHandler) : DelegatingHandler(innerHandler)
{
    private const int MaximumAttempts = 3;
    private static readonly TimeSpan[] Delays =
    [
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(750)
    ];

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        if (request.Method != HttpMethod.Get)
        {
            return await base.SendAsync(request, cancellationToken);
        }

        for (int attempt = 0; ; attempt++)
        {
            HttpRequestMessage attemptRequest = attempt == 0 ? request : CloneGetRequest(request);
            try
            {
                HttpResponseMessage response = await base.SendAsync(attemptRequest, cancellationToken);
                if (attempt >= MaximumAttempts - 1 || !IsTransient(response.StatusCode))
                {
                    return response;
                }

                TimeSpan delay = GetDelay(response, attempt);
                response.Dispose();
                await Task.Delay(delay, cancellationToken);
            }
            catch (HttpRequestException) when (attempt < MaximumAttempts - 1)
            {
                await Task.Delay(Delays[attempt], cancellationToken);
            }
            catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested && attempt < MaximumAttempts - 1)
            {
                await Task.Delay(Delays[attempt], cancellationToken);
            }
        }
    }

    private static HttpRequestMessage CloneGetRequest(HttpRequestMessage source)
    {
        var clone = new HttpRequestMessage(HttpMethod.Get, source.RequestUri)
        {
            Version = source.Version,
            VersionPolicy = source.VersionPolicy
        };
        foreach (KeyValuePair<string, IEnumerable<string>> header in source.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        return clone;
    }

    private static bool IsTransient(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.RequestTimeout or
        (HttpStatusCode)429 or
        HttpStatusCode.InternalServerError or
        HttpStatusCode.BadGateway or
        HttpStatusCode.ServiceUnavailable or
        HttpStatusCode.GatewayTimeout;

    private static TimeSpan GetDelay(HttpResponseMessage response, int attempt)
    {
        TimeSpan? retryAfter = response.Headers.RetryAfter?.Delta;
        if (retryAfter is { } serverDelay && serverDelay > TimeSpan.Zero)
        {
            return serverDelay <= TimeSpan.FromSeconds(5) ? serverDelay : TimeSpan.FromSeconds(5);
        }
        return Delays[attempt];
    }
}