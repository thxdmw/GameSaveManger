namespace GameSaveManager.Application.Updates;

/// <summary>保存不含凭据的更新检查偏好和最后成功检查时间。</summary>
public interface IUpdatePreferenceStore
{
    Task<ClientUpdatePreferences> LoadAsync(CancellationToken cancellationToken);
    Task SaveAsync(ClientUpdatePreferences preferences, CancellationToken cancellationToken);
}
