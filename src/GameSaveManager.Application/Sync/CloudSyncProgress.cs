namespace GameSaveManager.Application.Sync;

/// <summary>同步队列向 UI 报告的阶段与进度。</summary>
public sealed record CloudSyncProgress(string Stage, int Completed, int Total, string Message);