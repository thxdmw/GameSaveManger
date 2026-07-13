namespace GameSaveManager.Infrastructure.Persistence;

/// <summary>统一管理 V2 本地数据目录，禁止继续把运行数据写到 EXE 安装目录。</summary>
public static class AppDataPaths
{
    public static string RootDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GameSaveManager");

    public static string DataDirectory { get; } = Path.Combine(RootDirectory, "data");

    public static string DatabasePath { get; } = Path.Combine(DataDirectory, "gamesave.db");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(DataDirectory);
    }
}
