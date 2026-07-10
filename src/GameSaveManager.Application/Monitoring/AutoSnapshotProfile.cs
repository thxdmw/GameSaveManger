namespace GameSaveManager.Application.Monitoring;

/// <summary>自动快照需要的本机配置；路径仅留在当前设备，不会同步到云端。</summary>
public sealed record AutoSnapshotProfile(
    string ProcessName,
    string SaveDirectory);