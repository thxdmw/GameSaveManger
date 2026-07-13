using System.Security.Cryptography;
using System.Text;

namespace GameSaveManager.Application.Api;

/// <summary>GameSave 服务端地址校验、规范化与稳定标识生成工具。</summary>
public static class GameSaveServerIdentity
{
    /// <summary>
    /// 解析用户输入的服务端地址。远程地址必须使用 HTTPS；HTTP 仅允许 localhost/回环地址。
    /// Query 和 Fragment 不属于 API 基础地址，直接拒绝，避免相同服务端被解析出不一致的 API/凭据作用域。
    /// </summary>
    public static Uri ParseAndValidate(string address)
    {
        if (!Uri.TryCreate(address.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("服务器地址必须是有效的 http/https URL");
        }
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException("服务器基础地址不能包含 Query 或 Fragment");
        }
        if (uri.Scheme == Uri.UriSchemeHttp && !uri.IsLoopback)
        {
            throw new InvalidOperationException("远程 GameSave 服务端必须使用 HTTPS；HTTP 仅允许 localhost/回环地址");
        }
        return uri;
    }

    /// <summary>
    /// 返回稳定服务端身份：scheme/host 统一小写，基础路径保留原始大小写。
    /// </summary>
    public static string Normalize(Uri server)
    {
        ArgumentNullException.ThrowIfNull(server);

        string origin = server.GetComponents(
            UriComponents.SchemeAndServer,
            UriFormat.UriEscaped).ToLowerInvariant();
        string path = server.GetComponents(
            UriComponents.Path,
            UriFormat.UriEscaped).TrimEnd('/');
        return string.IsNullOrEmpty(path)
            ? origin
            : $"{origin}/{path.TrimStart('/')}";
    }

    /// <summary>用 SHA-256 把规范化服务端身份转换为适合本地持久化主键的固定长度字符串。</summary>
    public static string CreateStableKey(Uri server)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(server)));
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
