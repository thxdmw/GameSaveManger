using GameSaveManager.Application.Games;

namespace GameSaveManager.Application.Restores;

public enum RegistryRestoreState
{
    NotRequired,
    Preparing,
    Prepared,
    Applying,
    Applied,
    RollingBack,
    RolledBack,
    Failed
}

public sealed record RegistryRestorePreparation(
    string SafetyDirectory,
    string SnapshotDirectory,
    IReadOnlyList<RegistrySaveRule> Rules,
    RegistryRestoreState State);

/// <summary>将 Windows 注册表恢复纳入文件恢复事务，避免 Application 层直接依赖注册表 API。</summary>
public interface IRegistryRestoreTransaction
{
    Task<RegistryRestorePreparation> PrepareAsync(
        string snapshotDirectory,
        IReadOnlyList<RegistrySaveRule> rules,
        string transactionDirectory,
        CancellationToken cancellationToken);

    Task ApplyAsync(RegistryRestorePreparation preparation, CancellationToken cancellationToken);

    Task RollbackAsync(RegistryRestorePreparation preparation, CancellationToken cancellationToken);
}