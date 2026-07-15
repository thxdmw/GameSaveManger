using GameSaveManager.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;

internal static class SqliteSchemaVerification
{
    public static async Task VerifySchemaVersionAsync()
    {
        string directory = Path.Combine(
            Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string databasePath = Path.Combine(directory, "schema.db");
        try
        {
            var database = new SqliteDatabase(databasePath);
            await database.InitializeAsync(CancellationToken.None);
            await database.InitializeAsync(CancellationToken.None);

            await using SqliteConnection connection = database.CreateConnection();
            await connection.OpenAsync();
            await using SqliteCommand command = connection.CreateCommand();
            command.CommandText = "SELECT version FROM schema_version WHERE id = 1;";
            long version = Convert.ToInt64(await command.ExecuteScalarAsync());
            Ensure(version == 5, "本地数据库应被记录为当前 schema 版本。");
        }
        finally
        {
            TryDelete(databasePath);
            TryDelete(databasePath + "-wal");
            TryDelete(databasePath + "-shm");
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch (IOException) { }
    }
}