namespace GameSaveManager.Application.Monitoring;

/// <summary>自动快照需要的本机配置；支持同一游戏的多个实际运行进程。</summary>
public sealed record AutoSnapshotProfile(
    IReadOnlyList<string> ProcessNames,
    IReadOnlyList<string> SaveDirectories);