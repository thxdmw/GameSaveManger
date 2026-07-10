namespace GameSaveManager.Domain.Snapshots;

public enum SnapshotTrigger
{
    Manual,
    GameExit,
    BeforeRestore,
    Import
}
