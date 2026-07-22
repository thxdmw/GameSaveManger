namespace GameSaveManager.Application.Api;

/// <summary>在云端数据进入界面或本地状态前，验证身份、唯一性与字段间的一致性。</summary>
public static class CloudApiResponseValidator
{
    private const int MaximumGames = 10_000;
    private const int MaximumDevices = 10_000;

    public static void ValidateGames(IReadOnlyList<CloudGame> games)
    {
        ArgumentNullException.ThrowIfNull(games);
        if (games.Count > MaximumGames)
            throw new InvalidDataException("服务端返回的游戏数量超过客户端安全上限。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudGame? game in games)
        {
            ValidateGame(game);
            if (!ids.Add(game!.GameId))
                throw new InvalidDataException($"服务端返回了重复的游戏 ID：{game.GameId}");
            if (!names.Add(game.Name.Trim()))
                throw new InvalidDataException($"服务端返回了重复的游戏名称：{game.Name}");
        }
    }

    public static void ValidateCreatedGame(CloudGame? game, string expectedName, string expectedProvider)
    {
        ValidateGame(game);
        if (!string.Equals(game!.Name.Trim(), expectedName.Trim(), StringComparison.OrdinalIgnoreCase)
            || !string.Equals(game.Provider, expectedProvider, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("服务端返回的游戏身份与创建请求不一致。");
    }

    public static void ValidateRetentionPolicy(CloudRetentionPolicy? policy, string expectedGameId)
    {
        if (policy is null
            || !string.Equals(policy.GameId, expectedGameId, StringComparison.Ordinal)
            || policy.RetentionCount is < 1 or > 500
            || policy.RetentionDays is < 0 or > 3650)
            throw new InvalidDataException("服务端返回的快照保留策略与当前游戏不一致或数值无效。");
    }

    public static void ValidateRetentionCleanup(CloudRetentionCleanupResult? result, string expectedGameId)
    {
        if (result is null
            || !string.Equals(result.GameId, expectedGameId, StringComparison.Ordinal)
            || result.DeletedSnapshotCount < 0)
            throw new InvalidDataException("服务端返回的保留策略清理结果与当前游戏不一致。");
    }

    public static void ValidateQuota(CloudQuota? quota)
    {
        if (quota is null
            || quota.QuotaBytes < 0
            || quota.UsedBytes < 0
            || quota.RemainingBytes < 0
            || quota.RemainingBytes > quota.QuotaBytes
            || quota.RemainingBytes != Math.Max(0, quota.QuotaBytes - quota.UsedBytes))
            throw new InvalidDataException("服务端返回的存储配额数据不一致。");
    }

    public static void ValidateDevices(IReadOnlyList<CloudDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);
        if (devices.Count > MaximumDevices)
            throw new InvalidDataException("服务端返回的设备数量超过客户端安全上限。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (CloudDevice? device in devices)
        {
            if (device is null)
                throw new InvalidDataException("服务端返回了空设备记录。");
            ValidateText(device.DeviceId, "设备 ID", 256);
            ValidateText(device.DeviceName, "设备名称", 256);
            if (!ids.Add(device.DeviceId))
                throw new InvalidDataException($"服务端返回了重复的设备 ID：{device.DeviceId}");
        }
    }

    public static void ValidateSnapshots(IReadOnlyList<CloudSnapshotSummary> snapshots, int requestedLimit)
    {
        ArgumentNullException.ThrowIfNull(snapshots);
        int safeLimit = Math.Clamp(requestedLimit, 1, GameSaveProtocolLimits.MaximumSnapshotListLimit);
        if (snapshots.Count > safeLimit)
            throw new InvalidDataException("服务端返回的快照数量超过请求上限。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (CloudSnapshotSummary? snapshot in snapshots)
        {
            if (snapshot is null)
                throw new InvalidDataException("服务端返回了空快照记录。");
            ValidateText(snapshot.SnapshotId, "快照 ID", 256);
            ValidateOptionalText(snapshot.ParentSnapshotId, "父快照 ID", 256);
            ValidateText(snapshot.DeviceId, "快照设备 ID", 256);
            ValidateText(snapshot.TriggerType, "快照触发类型", 64);
            ValidateOptionalText(snapshot.Description, "快照说明", GameSaveProtocolLimits.DescriptionMaxLength);
            if (!ids.Add(snapshot.SnapshotId))
                throw new InvalidDataException($"服务端返回了重复的快照 ID：{snapshot.SnapshotId}");
            if (string.Equals(snapshot.SnapshotId, snapshot.ParentSnapshotId, StringComparison.Ordinal)
                || snapshot.FileCount is < 0 or > GameSaveProtocolLimits.MaximumManifestFiles
                || snapshot.LogicalSize < 0
                || snapshot.ChangedFileCount < 0)
                throw new InvalidDataException($"快照 {snapshot.SnapshotId} 的摘要字段无效。");
            ValidateSnapshotRoots(snapshot);
        }
    }

    public static void ValidateManifest(
        CloudSnapshotManifest? manifest,
        string expectedGameId,
        string expectedSnapshotId)
    {
        if (manifest is null
            || !string.Equals(manifest.GameId, expectedGameId, StringComparison.Ordinal)
            || !string.Equals(manifest.SnapshotId, expectedSnapshotId, StringComparison.Ordinal)
            || manifest.Files is null)
            throw new InvalidDataException("服务端返回的完整快照身份与请求不一致。");
        ValidateText(manifest.SnapshotId, "快照 ID", 256);
        ValidateText(manifest.GameId, "快照游戏 ID", 256);
        ValidateText(manifest.DeviceId, "快照设备 ID", 256);
        ValidateText(manifest.TriggerType, "快照触发类型", 64);
        ValidateOptionalText(manifest.ParentSnapshotId, "父快照 ID", 256);
        ValidateOptionalText(manifest.Description, "快照说明", GameSaveProtocolLimits.DescriptionMaxLength);
        if (manifest.Files.Count > GameSaveProtocolLimits.MaximumManifestFiles)
            throw new InvalidDataException("服务端快照超过客户端允许的文件数量上限。");

        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long logicalSize = 0;
        foreach (CloudSnapshotFile? file in manifest.Files)
        {
            if (file is null)
                throw new InvalidDataException("服务端快照包含空文件记录。");
            string normalizedPath = file.RelativePath?.Replace('\\', '/') ?? string.Empty;
            string[] segments = normalizedPath.Split('/');
            if (normalizedPath.Length == 0
                || normalizedPath.Length > GameSaveProtocolLimits.RelativePathMaxLength
                || Path.IsPathRooted(normalizedPath)
                || segments.Any(segment => segment is "" or "." or "..")
                || !paths.Add(normalizedPath))
                throw new InvalidDataException("服务端快照包含无效、越界或重复的文件路径。");
            ValidateText(file.ObjectId, "内容对象 ID", 256);
            if (file.Size < 0
                || string.IsNullOrWhiteSpace(file.Sha256)
                || file.Sha256.Length != 64
                || file.Sha256.Any(character => !Uri.IsHexDigit(character)))
                throw new InvalidDataException($"服务端快照包含无效的内容描述：{file.RelativePath}");
            try { logicalSize = checked(logicalSize + file.Size); }
            catch (OverflowException exception)
            {
                throw new InvalidDataException("服务端快照声明的总大小超过客户端可处理范围。", exception);
            }
        }

        if (manifest.Roots is not { } roots) return;
        if (roots.Count > GameSaveProtocolLimits.MaximumSnapshotRoots)
            throw new InvalidDataException("服务端快照超过客户端允许的根目录数量上限。");
        var rootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudSnapshotRoot? root in roots)
        {
            if (root is null)
                throw new InvalidDataException("服务端快照包含空根目录记录。");
            ValidateText(root.RootId, "快照根目录 ID", GameSaveProtocolLimits.RootIdMaxLength);
            if (root.RootId.Any(character => !char.IsAsciiLetterOrDigit(character)
                                             && character is not '_' and not '-')
                || !rootIds.Add(root.RootId)
                || !(string.Equals(root.RootType, "FILE", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(root.RootType, "REGISTRY", StringComparison.OrdinalIgnoreCase))
                || root.Confidence is < 0 or > 100)
                throw new InvalidDataException("服务端快照包含无效或重复的根目录描述。");
            ValidateOptionalText(root.PathTemplate, "快照路径模板", GameSaveProtocolLimits.PathTemplateMaxLength);
            ValidateText(root.Source, "快照根目录来源", 32);
            ValidatePatterns(root.IncludePatterns, manifest.SnapshotId);
            ValidatePatterns(root.ExcludePatterns, manifest.SnapshotId);
        }
        if (rootIds.Count > 0 && manifest.Files.Any(file =>
                !rootIds.Contains(file.RelativePath.Replace('\\', '/').Split('/')[0])))
            throw new InvalidDataException("服务端快照文件引用了未声明的根目录。");
    }

    private static void ValidateGame(CloudGame? game)
    {
        if (game is null)
            throw new InvalidDataException("服务端返回了空游戏记录。");
        ValidateText(game.GameId, "游戏 ID", 256);
        ValidateText(game.Name, "游戏名称", 256);
        ValidateText(game.Provider, "游戏平台", 64);
        ValidateOptionalText(game.ProviderGameId, "平台游戏 ID", 512);
    }

    private static void ValidateSnapshotRoots(CloudSnapshotSummary snapshot)
    {
        if (snapshot.Roots is not { } roots) return;
        if (roots.Count > GameSaveProtocolLimits.MaximumSnapshotRoots)
            throw new InvalidDataException($"快照 {snapshot.SnapshotId} 的根目录数量超过安全上限。");

        var rootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CloudSnapshotRoot? root in roots)
        {
            if (root is null)
                throw new InvalidDataException($"快照 {snapshot.SnapshotId} 包含空根目录记录。");
            ValidateText(root.RootId, "快照根目录 ID", GameSaveProtocolLimits.RootIdMaxLength);
            ValidateText(root.RootType, "快照根目录类型", 32);
            ValidateText(root.Source, "快照根目录来源", 64);
            ValidateOptionalText(root.PathTemplate, "快照路径模板", GameSaveProtocolLimits.PathTemplateMaxLength);
            if (!rootIds.Add(root.RootId)
                || root.Confidence is < 0 or > 100
                || !(string.Equals(root.RootType, "FILE", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(root.RootType, "REGISTRY", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException($"快照 {snapshot.SnapshotId} 包含无效或重复的根目录。");
            ValidatePatterns(root.IncludePatterns, snapshot.SnapshotId);
            ValidatePatterns(root.ExcludePatterns, snapshot.SnapshotId);
        }
    }

    private static void ValidatePatterns(IReadOnlyList<string>? patterns, string snapshotId)
    {
        if (patterns is null) return;
        if (patterns.Count > GameSaveProtocolLimits.MaximumPatternsPerRoot)
            throw new InvalidDataException($"快照 {snapshotId} 的扫描规则数量超过安全上限。");
        foreach (string? pattern in patterns)
            ValidateText(pattern, "快照扫描规则", GameSaveProtocolLimits.PatternMaxLength);
    }

    private static void ValidateOptionalText(string? value, string fieldName, int maximumLength)
    {
        if (value is null) return;
        if (value.Length > maximumLength || value.IndexOf('\0') >= 0)
            throw new InvalidDataException($"服务端返回的{fieldName}无效。");
    }

    private static void ValidateText(string? value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.IndexOf('\0') >= 0)
            throw new InvalidDataException($"服务端返回的{fieldName}无效。");
    }
}
