namespace GameSaveManager.Application.Games;

/// <summary>把明确配置的注册表键导出为受控 JSON 文件，并在恢复后导入。</summary>
public interface IRegistrySaveSnapshotService
{
    Task ExportAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken);
    Task ImportAsync(string directory, IReadOnlyList<RegistrySaveRule> rules, CancellationToken cancellationToken);
}
