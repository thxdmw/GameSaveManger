using System.Security.Cryptography;
using System.Text;

namespace GameSaveManager.Application.Security;

/// <summary>生成 Windows Credential Manager 使用的稳定凭据名称。</summary>
public static class CredentialTargets
{
    /// <summary>
    /// 设备 Token 按服务端地址隔离，避免切换 GameSave 服务端时把 A 服务签发的 Bearer Token 发给 B 服务。
    /// scheme 和 host 统一小写；URL path 保留原始大小写，因为服务端路径可能区分大小写。
    /// </summary>
    public static string ForDeviceToken(Uri server)
    {
        ArgumentNullException.ThrowIfNull(server);

        string origin = server.GetComponents(
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped).ToLowerInvariant();
        string path = server.GetComponents(
            UriComponents.Path,
            UriFormat.UriEscaped).TrimEnd('/');
        string normalizedServer = string.IsNullOrEmpty(path)
            ? origin
            : $"{origin}/{path.TrimStart('/')}";

        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedServer));
        return $"GameSaveManager/DeviceToken/{Convert.ToHexString(hash).ToLowerInvariant()}";
    }
}
