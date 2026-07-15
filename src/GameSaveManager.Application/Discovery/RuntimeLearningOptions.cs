namespace GameSaveManager.Application.Discovery;

/// <summary>运行学习的受限元数据扫描策略；不读取文件内容，也不计算哈希。</summary>
public sealed record RuntimeLearningOptions(
    int MaxDepth = 6,
    int MaxFilesPerRoot = 100000,
    long MaxTotalFiles = 300000,
    IReadOnlyList<string>? ExcludedDirectoryNames = null)
{
    public IReadOnlyList<string> EffectiveExcludedDirectoryNames => ExcludedDirectoryNames ??
        ["Cache", "Caches", "Temp", "Logs", "Crash", "CrashReports", "ShaderCache", "GPUCache", "Code Cache", "Service Worker", "node_modules", "$Recycle.Bin"];
}
