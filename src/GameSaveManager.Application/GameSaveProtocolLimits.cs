namespace GameSaveManager.Application;

/// <summary>客户端与 CMS 共同遵守的协议边界；服务端通过契约测试保持相同数值。</summary>
public static class GameSaveProtocolLimits
{
    public const int MaximumManifestFiles = 5000;
    public const int MaximumSnapshotListLimit = 200;
    public const int RelativePathMaxLength = 1024;
    public const int DescriptionMaxLength = 500;
    public const int MaximumSnapshotRoots = 32;
    public const int RootIdMaxLength = 64;
    public const int PathTemplateMaxLength = 1024;
    public const int MaximumPatternsPerRoot = 64;
    public const int PatternMaxLength = 256;

    public const string ManifestFileLimitMessage =
        "当前存档包含超过 5000 个文件，请通过包含/排除规则移除缓存、日志或无关文件。";
}
