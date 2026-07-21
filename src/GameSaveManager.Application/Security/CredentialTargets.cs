using GameSaveManager.Application.Api;

namespace GameSaveManager.Application.Security;

/// <summary>生成 Windows Credential Manager 使用的稳定凭据名称。</summary>
public static class CredentialTargets
{
    /// <summary>当前 Windows 用户下稳定的客户端设备标识，不随 AppData 清理而丢失。</summary>
    public const string StableDeviceId = "GameSaveManager/DeviceIdentity/v1";

    /// <summary>当前服务端活动账号的稳定 userId。</summary>
    public static string ForAccountUserId(Uri server) =>
        $"GameSaveManager/AccountUserId/{GameSaveServerIdentity.CreateStableKey(server)}";
    /// <summary>设备 Token 按服务端地址隔离，避免把一个服务端的 Bearer Token 发给另一个服务端。</summary>
    public static string ForDeviceToken(Uri server) =>
        $"GameSaveManager/DeviceToken/{GameSaveServerIdentity.CreateStableKey(server)}";

    /// <summary>登录账号名称按服务端隔离保存，仅用于恢复已登录界面的展示信息。</summary>
    public static string ForAccountName(Uri server) =>
        $"GameSaveManager/AccountName/{GameSaveServerIdentity.CreateStableKey(server)}";
}
