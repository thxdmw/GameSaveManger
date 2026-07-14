using GameSaveManager.Application.Settings;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>将最近一次成功登录的服务器地址保存到用户本地数据目录；不写入任何凭据。</summary>
public sealed class TextFileServerAddressStore : IServerAddressStore
{
    private readonly string _path;

    public TextFileServerAddressStore(string? path = null) =>
        _path = path ?? Path.Combine(AppDataPaths.RootDirectory, "server-address.txt");

    public async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path)) return null;
        string value = await File.ReadAllTextAsync(_path, cancellationToken);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public async Task SaveAsync(string serverAddress, CancellationToken cancellationToken)
    {
        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(_path, serverAddress.Trim(), cancellationToken);
    }
}