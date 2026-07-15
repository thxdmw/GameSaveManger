namespace GameSaveManager.Application.Restores;

/// <summary>恢复事务的持久化状态；任何目录移动前都会先写入日志。</summary>
public enum RestoreJournalState
{
    Prepared,
    OriginalMoved,
    Applied,
    Completed
}

/// <summary>
/// 崩溃恢复所需的最小事实记录。
/// 临时目录和安全备份均使用绝对路径，重启时无需依赖当时的 UI 状态。
/// </summary>
public sealed record RestoreJournal(
    string TransactionId,
    RestoreJournalState State,
    string SaveDirectory,
    string StagingDirectory,
    string? SafetyBackupDirectory,
    string SnapshotId,
    DateTimeOffset UpdatedAt);

/// <summary>一次恢复的最终结果；安全备份保留在原存档目录旁，便于用户主动核验后清理。</summary>
public sealed record RestoreResult(
    string SnapshotId,
    string SaveDirectory,
    string? SafetyBackupDirectory);
public enum MultiRootRestoreState { Prepared, StagingBuilt, OriginalsMoved, TargetsApplied, Verified, Completed, RollingBack, RolledBack, Failed }
public enum RestoreRootState { Prepared, StagingBuilding, StagingBuilt, MovingOriginal, OriginalMoved, ApplyingTarget, Applied, Verifying, Verified, RollingBack, RolledBack, Failed }
public sealed record RestoreRootJournalItem(string RootId, string TargetDirectory, string StagingDirectory, string SafetyBackupDirectory, RestoreRootState State, bool OriginalExisted, bool OriginalMoved, bool TargetApplied);
public sealed record MultiRootRestoreJournal(string TransactionId, string GameId, string SnapshotId, MultiRootRestoreState State, IReadOnlyList<RestoreRootJournalItem> Roots, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);