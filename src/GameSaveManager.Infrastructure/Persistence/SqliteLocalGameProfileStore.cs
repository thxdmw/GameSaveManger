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
        foreach (string gameId in ownedGameIds.Distinct(StringComparer.Ordinal))
        {
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "UPDATE local_game_profile SET account_id = $userId WHERE server_key = $serverKey AND account_id = '' AND game_id = $gameId;";
            command.Parameters.AddWithValue("$serverKey", serverKey);
            command.Parameters.AddWithValue("$userId", userId);
            command.Parameters.AddWithValue("$gameId", gameId);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
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

    private static T? Deserialize<T>(string? json) where T : class { try { return string.IsNullOrWhiteSpace(json) ? null : JsonSerializer.Deserialize<T>(json); } catch (JsonException) { return null; } }

    private static LocalGameProfile Read(SqliteDataReader reader) => new(
        reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3),
        reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetString(5), reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetString(7),
        Enum.TryParse(reader.GetString(8), out SaveLocationSource source) ? source : SaveLocationSource.Manual,
        reader.GetInt32(9), reader.GetInt64(12) != 0, reader.GetInt64(13) != 0,
        Deserialize<List<SaveRootRule>>(reader.IsDBNull(10) ? null : reader.GetString(10)), Deserialize<List<RegistrySaveRule>>(reader.IsDBNull(11) ? null : reader.GetString(11)),
        reader.IsDBNull(14) ? null : reader.GetString(14), Deserialize<GameLaunchProfile>(reader.IsDBNull(15) ? null : reader.GetString(15)),
        reader.IsDBNull(16) ? string.Empty : reader.GetString(16));
}
