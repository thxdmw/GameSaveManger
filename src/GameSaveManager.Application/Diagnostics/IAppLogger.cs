namespace GameSaveManager.Application.Diagnostics;

/// <summary>客户端结构化日志抽象；调用方禁止传入密码、设备 Token 或请求正文。</summary>
public interface IAppLogger
{
    void Information(string eventName, string message);

    void Error(string eventName, Exception exception, string message);
}