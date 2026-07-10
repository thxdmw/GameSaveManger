using GameSaveManager.Application.Device;
using Microsoft.Data.Sqlite;

namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>通过 SQLite 非敏感设置表持久化本机稳定 deviceId。</summary>
public sealed class SqliteDeviceIdentityProvider(SqliteDatabase database) : IDeviceIdentityProvider
{
    private const string DeviceIdKey = "device_id";

    public async Task<string> GetOrCreateDeviceIdAsync(CancellationToken cancellationToken)
    {
        string? existing = await ReadAsync(cancellationToken);
        if (!string.IsNullOrWhiteSpace(existing))
        {
            return existing;
        }

        string candidate = Guid.NewGuid().ToString("N");
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO client_setting(setting_key, setting_value)
            VALUES($key, $value);
            """;
        command.Parameters.AddWithValue("$key", DeviceIdKey);
        command.Parameters.AddWithValue("$value", candidate);
        await command.ExecuteNonQueryAsync(cancellationToken);

        return await ReadAsync(cancellationToken)
            ?? throw new InvalidOperationException("deviceId 持久化失败");
    }

    private async Task<string?> ReadAsync(CancellationToken cancellationToken)
    {
        await using SqliteConnection connection = database.CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT setting_value
            FROM client_setting
            WHERE setting_key = $key
            LIMIT 1;
            """;
        command.Parameters.AddWithValue("$key", DeviceIdKey);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }
}
