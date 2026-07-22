using GameSaveManager.Application;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Launching;
using Microsoft.Data.Sqlite;
using System.Text.Json;

namespace GameSaveManager.Infrastructure.Persistence;

public sealed class SqliteLocalGameProfileStore(SqliteDatabase database) : ILocalGameProfileStore
{
    public async Task<LocalGameProfile?> GetAsync(string serverKey, string userId, string gameId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = CreateSelectCommand(connection, "WHERE server_key = $serverKey AND account_id = $userId AND game_id = $gameId LIMIT 1;");
        command.Parameters.AddWithValue("$serverKey", serverKey);
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$gameId", gameId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<LocalGameProfile>> ListAsync(string serverKey, string userId, CancellationToken cancellationToken)
    {
        var profiles = new List<LocalGameProfile>();
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = CreateSelectCommand(connection, "WHERE server_key = $serverKey AND account_id = $userId;");
        command.Parameters.AddWithValue("$serverKey", serverKey);
        command.Parameters.AddWithValue("$userId", userId);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) profiles.Add(Read(reader));
        return profiles;
    }

    public async Task ClaimLegacyAsync(string serverKey, string userId, IReadOnlyCollection<string> ownedGameIds, CancellationToken cancellationToken)
    {
        if (ownedGameIds.Count == 0) return;
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (string gameId in ownedGameIds.Distinct(StringComparer.Ordinal))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                DELETE FROM local_game_profile
                WHERE server_key = $serverKey AND account_id = '' AND game_id = $gameId
                  AND EXISTS (
                      SELECT 1 FROM local_game_profile current
                      WHERE current.server_key = $serverKey
                        AND current.account_id = $userId
                        AND current.game_id = $gameId);
                UPDATE local_game_profile
                SET account_id = $userId
                WHERE server_key = $serverKey AND account_id = '' AND game_id = $gameId;
                """;
            command.Parameters.AddWithValue("$serverKey", serverKey);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$gameId", gameId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task DeleteAsync(string serverKey, string userId, string gameId, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "DELETE FROM local_game_profile WHERE server_key = $serverKey AND account_id = $userId AND game_id = $gameId;";
        command.Parameters.AddWithValue("$serverKey", serverKey);
        command.Parameters.AddWithValue("$userId", userId);
        command.Parameters.AddWithValue("$gameId", gameId);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken)
    {
        ValidateProfile(profile);
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO local_game_profile(server_key, account_id, game_id, provider, provider_game_id, install_directory, save_directory,
                process_name, executable_path, save_directory_source, save_directory_confidence, save_roots_json, registry_save_rules_json, user_confirmed, auto_snapshot_enabled, identity_executable_path, launch_profile_json, update_time_utc)
            VALUES($serverKey, $userId, $gameId, $provider, $providerGameId, $installDirectory, $saveDirectory,
                $processName, $executablePath, $source, $confidence, $roots, $registryRules, $confirmed, $enabled, $identityExecutablePath, $launchProfile, $updatedAt)
            ON CONFLICT(server_key, account_id, game_id) DO UPDATE SET
                provider = excluded.provider, provider_game_id = excluded.provider_game_id, install_directory = excluded.install_directory,
                save_directory = excluded.save_directory, process_name = excluded.process_name, executable_path = excluded.executable_path,
                save_directory_source = excluded.save_directory_source, save_directory_confidence = excluded.save_directory_confidence, save_roots_json = excluded.save_roots_json, registry_save_rules_json = excluded.registry_save_rules_json,
                user_confirmed = excluded.user_confirmed, auto_snapshot_enabled = excluded.auto_snapshot_enabled, identity_executable_path = excluded.identity_executable_path,
                launch_profile_json = excluded.launch_profile_json, update_time_utc = excluded.update_time_utc;
            """;
        command.Parameters.AddWithValue("$serverKey", profile.ServerKey);
        command.Parameters.AddWithValue("$userId", profile.UserId);
        command.Parameters.AddWithValue("$gameId", profile.GameId);
        command.Parameters.AddWithValue("$provider", profile.Provider);
        command.Parameters.AddWithValue("$providerGameId", (object?)profile.ProviderGameId ?? DBNull.Value);
        command.Parameters.AddWithValue("$installDirectory", (object?)profile.InstallDirectory ?? DBNull.Value);
        command.Parameters.AddWithValue("$saveDirectory", profile.SaveDirectory);
        command.Parameters.AddWithValue("$processName", profile.ProcessName);
        command.Parameters.AddWithValue("$executablePath", (object?)profile.ExecutablePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$source", profile.SaveDirectorySource.ToString());
        command.Parameters.AddWithValue("$confidence", profile.SaveDirectoryConfidence);
        command.Parameters.AddWithValue("$roots", JsonSerializer.Serialize(profile.EffectiveSaveRoots));
        command.Parameters.AddWithValue("$registryRules", JsonSerializer.Serialize(profile.EffectiveRegistrySaveRules));
        command.Parameters.AddWithValue("$confirmed", profile.UserConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$enabled", profile.AutoSnapshotEnabled && profile.UserConfirmed ? 1 : 0);
        command.Parameters.AddWithValue("$identityExecutablePath", (object?)profile.IdentityExecutablePath ?? DBNull.Value);
        command.Parameters.AddWithValue("$launchProfile", profile.EffectiveLaunchProfile is null ? DBNull.Value : JsonSerializer.Serialize(profile.EffectiveLaunchProfile));
        command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static SqliteCommand CreateSelectCommand(SqliteConnection connection, string whereClause)
    {
        SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT server_key, game_id, provider, provider_game_id, install_directory, save_directory, process_name, executable_path, save_directory_source, save_directory_confidence, save_roots_json, registry_save_rules_json, user_confirmed, auto_snapshot_enabled, identity_executable_path, launch_profile_json, account_id FROM local_game_profile " + whereClause;
        return command;
    }

    private static T? Deserialize<T>(string? json, string fieldName) where T : class
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(json)
                   ?? throw new InvalidDataException($"本机游戏配置字段 {fieldName} 为空对象。");
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException($"本机游戏配置字段 {fieldName} 已损坏。为避免漏备份，客户端已停止加载该配置。", exception);
        }
    }

    private static LocalGameProfile Read(SqliteDataReader reader)
    {
        var profile = new LocalGameProfile(
            reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
            reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
            ParseSaveLocationSource(reader.GetString(8)),
            reader.GetInt32(9), ReadBoolean(reader, 12, "用户确认状态"), ReadBoolean(reader, 13, "自动同步状态"),
            Deserialize<List<SaveRootRule>>(reader.IsDBNull(10) ? null : reader.GetString(10), "save_roots_json"), Deserialize<List<RegistrySaveRule>>(reader.IsDBNull(11) ? null : reader.GetString(11), "registry_save_rules_json"),
            reader.IsDBNull(14) ? null : reader.GetString(14), Deserialize<GameLaunchProfile>(reader.IsDBNull(15) ? null : reader.GetString(15), "launch_profile_json"),
            reader.IsDBNull(16) ? string.Empty : reader.GetString(16));
        ValidateProfile(profile);
        return profile;
    }

    private static void ValidateProfile(LocalGameProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ValidateText(profile.ServerKey, "服务端标识", 1024);
        ValidateText(profile.UserId, "账号 ID", 256);
        ValidateText(profile.GameId, "游戏 ID", 256);
        ValidateText(profile.Provider, "游戏平台", 64);
        ValidateOptionalText(profile.ProviderGameId, "平台游戏 ID", 512);
        ValidateOptionalPath(profile.InstallDirectory, "安装目录");
        ValidatePath(profile.SaveDirectory, "存档目录");
        ValidateOptionalText(profile.ProcessName, "进程名", 260);
        ValidateOptionalPath(profile.ExecutablePath, "启动程序路径");
        ValidateOptionalPath(profile.IdentityExecutablePath, "身份程序路径");
        if (!Enum.IsDefined(profile.SaveDirectorySource) || profile.SaveDirectoryConfidence is < 0 or > 100)
            throw Corrupted("存档来源或置信度");

        IReadOnlyList<SaveRootRule> roots = profile.SaveRoots ?? [];
        IReadOnlyList<RegistrySaveRule> registryRules = profile.RegistrySaveRules ?? [];
        if (roots.Count > GameSaveProtocolLimits.MaximumSnapshotRoots
            || registryRules.Count > GameSaveProtocolLimits.MaximumSnapshotRoots)
            throw Corrupted("存档根目录数量");

        var rootIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (SaveRootRule? root in roots)
        {
            if (root is null) throw Corrupted("存档根目录");
            ValidateRootId(root.RootId, rootIds);
            ValidatePath(root.Path, $"存档根目录 {root.RootId}");
            if (!Enum.IsDefined(root.Source) || root.Confidence is < 0 or > 100)
                throw Corrupted($"存档根目录 {root.RootId} 的来源或置信度");
            ValidatePatterns(root.IncludePatterns, root.RootId);
            ValidatePatterns(root.ExcludePatterns, root.RootId);
        }

        var registryRuleIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var registryKeyPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RegistrySaveRule? rule in registryRules)
        {
            if (rule is null) throw Corrupted("注册表存档规则");
            ValidateRootId(rule.RuleId, registryRuleIds);
            ValidateText(rule.KeyPath, $"注册表规则 {rule.RuleId} 的键路径", 1024);
            if (!(rule.KeyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase)
                  || rule.KeyPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)))
                throw Corrupted($"注册表规则 {rule.RuleId} 的键路径");
            if (!registryKeyPaths.Add(rule.KeyPath))
                throw Corrupted("重复的注册表键路径");
        }

        if (profile.LaunchProfile is { } launch)
        {
            if (!Enum.IsDefined(launch.TargetType)) throw Corrupted("启动入口类型");
            ValidateText(launch.Target, "启动入口", 32767);
            ValidateOptionalText(launch.Arguments, "启动参数", 32767);
            ValidateOptionalPath(launch.WorkingDirectory, "启动工作目录");
            ValidateOptionalText(launch.ShortcutArguments, "快捷方式参数", 32767);
            if (launch.MonitoredProcessNames is null || launch.MonitoredProcessNames.Count > 64)
                throw Corrupted("监控进程名列表");
            foreach (string? processName in launch.MonitoredProcessNames)
                ValidateText(processName, "监控进程名", 260);
        }
    }

    private static void ValidateRootId(string? rootId, ISet<string> rootIds)
    {
        ValidateText(rootId, "存档根目录 ID", GameSaveProtocolLimits.RootIdMaxLength);
        string validRootId = rootId!;
        if (validRootId.Any(character => !char.IsAsciiLetterOrDigit(character)
                                         && character is not '_' and not '-')
            || !rootIds.Add(validRootId))
            throw Corrupted("存档根目录 ID");
    }

    private static SaveLocationSource ParseSaveLocationSource(string value)
    {
        if (!Enum.TryParse(value, ignoreCase: true, out SaveLocationSource source)
            || !Enum.IsDefined(source))
            throw Corrupted("存档来源");
        return source;
    }

    private static bool ReadBoolean(SqliteDataReader reader, int ordinal, string fieldName)
    {
        long value = reader.GetInt64(ordinal);
        if (value is not 0 and not 1) throw Corrupted(fieldName);
        return value == 1;
    }

    private static void ValidatePatterns(IReadOnlyList<string>? patterns, string rootId)
    {
        if (patterns is null || patterns.Count > GameSaveProtocolLimits.MaximumPatternsPerRoot)
            throw Corrupted($"存档根目录 {rootId} 的扫描规则");
        foreach (string? pattern in patterns)
            ValidateText(pattern, $"存档根目录 {rootId} 的扫描规则", GameSaveProtocolLimits.PatternMaxLength);
    }

    private static void ValidatePath(string? path, string fieldName)
    {
        ValidateText(path, fieldName, 32767);
        if (!Path.IsPathFullyQualified(path!)) throw Corrupted(fieldName);
    }

    private static void ValidateOptionalPath(string? path, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(path)) return;
        ValidatePath(path, fieldName);
    }

    private static void ValidateOptionalText(string? value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        if (value.Length > maximumLength || value.IndexOf('\0') >= 0) throw Corrupted(fieldName);
    }

    private static void ValidateText(string? value, string fieldName, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value)
            || value.Length > maximumLength
            || value.IndexOf('\0') >= 0)
            throw Corrupted(fieldName);
    }

    private static InvalidDataException Corrupted(string fieldName) =>
        new($"本机游戏配置字段 {fieldName} 已损坏。为避免错绑游戏或漏备份，客户端已停止加载该配置。");
}
