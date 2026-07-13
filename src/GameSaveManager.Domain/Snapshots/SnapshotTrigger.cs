namespace GameSaveManager.Domain.Snapshots;

/// <summary>快照创建原因；服务端保存对应稳定大写枚举字符串。</summary>
public enum SnapshotTrigger
{
    /// <summary>用户主动创建或主动同步。</summary>
    Manual,

    /// <summary>检测到游戏进程退出后自动创建。</summary>
    GameExit,

    /// <summary>覆盖真实存档前创建的安全快照。</summary>
    BeforeRestore,

    /// <summary>从旧版备份或外部存档包导入。</summary>
    Import
}
