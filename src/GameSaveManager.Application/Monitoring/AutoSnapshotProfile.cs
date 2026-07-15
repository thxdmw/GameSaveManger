namespace GameSaveManager.Application.Monitoring;

/// <summary>自动快照需要的本机配置；所有已确认的文件存档根目录都必须被监听。</summary>
public sealed record AutoSnapshotProfile(
    string ProcessName,
    IReadOnlyList<string> SaveDirectories);
