namespace GameSaveManager.Application.Api;

/// <summary>GameSave 服务端返回的稳定业务错误。</summary>
public sealed class GameSaveApiException(int statusCode, string code, string message)
    : Exception(message)
{
    public int StatusCode { get; } = statusCode;

    public string Code { get; } = code;
}
