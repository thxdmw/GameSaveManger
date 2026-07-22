namespace GameSaveManager.Application.Discovery;

public enum SaveLocationSource
{
    LudusaviManifest,
    StoreMetadata,
    InstallDirectory,
    RuntimeLearning,
    CloudHistory,
    Heuristic,
    Manual
}

/// <summary>一个可供用户确认的存档位置候选，并附带检测依据与实际内容摘要。</summary>
public sealed record SaveLocationCandidate(
    string Path,
    int Confidence,
    SaveLocationSource Source,
    string Reason,
    int FileCount,
    long TotalSize,
    DateTime? LatestWriteTimeUtc,
    IReadOnlyList<string> SampleFiles,
    bool RequiresUserConfirmation);

public sealed record SaveDetectionProgress(string Stage, string Message, int Completed, int Total);
