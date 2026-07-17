using System.Text.Json;
using GameSaveManager.Application.Updates;
using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>将更新检查偏好保存到现有 client_setting 表。</summary>
public sealed class SqliteUpdatePreferenceStore(SqliteDatabase database) : IUpdatePreferenceStore
{
    private const string SettingKey = "client_update_preferences_v1";

    public async Task<ClientUpdatePreferences> LoadAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT setting_value FROM client_setting WHERE setting_key = $key LIMIT 1;";
        command.Parameters.AddWithValue("$key", SettingKey);
        string? json = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (string.IsNullOrWhiteSpace(json)) return ClientUpdatePreferences.Default;
        try
        {
            return JsonSerializer.Deserialize<ClientUpdatePreferences>(json)
                ?? ClientUpdatePreferences.Default;
        }
        catch (JsonException)
        {
            return ClientUpdatePreferences.Default;
        }
    }

    public async Task SaveAsync(ClientUpdatePreferences preferences, CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO client_setting(setting_key, setting_value)
            VALUES($key, $value)
            ON CONFLICT(setting_key) DO UPDATE SET setting_value = excluded.setting_value;
            """;
        command.Parameters.AddWithValue("$key", SettingKey);
        command.Parameters.AddWithValue("$value", JsonSerializer.Serialize(preferences));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
