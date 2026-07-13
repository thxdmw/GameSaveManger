namespace GameSaveManager.Domain.Games;

/// <summary>设备本地安装信息；路径不上传为跨设备共享游戏元数据。</summary>
public sealed record GameInstallation(
    string GameId,
    string Name,
    string InstallDirectory,
    string ExecutablePath,
    string SaveDirectory,
    string ProcessName);
