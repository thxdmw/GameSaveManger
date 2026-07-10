using GameSaveManager.Application.Api;

namespace GameSaveManager.Application.Security;

/// <summary>生成 Windows Credential Manager 使用的稳定凭据名称。</summary>
public static class CredentialTargets
{
    /// <summary>
    /// 设备 Token 按服务端地址隔离，避免切换 GameSave 服务端时把 A 服务签发的 Bearer Token 发给 B 服务。
    /// </summary>
    public static string ForDeviceToken(Uri server) =>
        $"GameSaveManager/DeviceToken/{GameSaveServerIdentity.CreateStableKey(server)}";
}
