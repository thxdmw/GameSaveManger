using System.Text.Json;
using GameSaveManager.Application.Restores;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.Verification;

internal static class RestoreRecoveryVerification
{
    public static async Task VerifyMultiRootJournalRecoveryAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
        string transactionDirectory = Path.Combine(root, "transaction");
        string targetA = Path.Combine(root, "save-a");
        string safetyA = Path.Combine(root, "safety-a");
        string stagingA = Path.Combine(root, "staging-a");
        string targetB = Path.Combine(root, "save-b");
        string safetyB = Path.Combine(root, "safety-b");
        string stagingB = Path.Combine(root, "staging-b");
        string journalPath = Path.Combine(transactionDirectory, "multi-root-journal.json");
        try
        {
            Directory.CreateDirectory(transactionDirectory);
            Directory.CreateDirectory(targetA);
            Directory.CreateDirectory(safetyA);
            await File.WriteAllTextAsync(Path.Combine(targetA, "new.txt"), "new");
            await File.WriteAllTextAsync(Path.Combine(safetyA, "old.txt"), "old");
            Directory.CreateDirectory(safetyB);
            await File.WriteAllTextAsync(Path.Combine(safetyB, "old.txt"), "old");

            var journal = new MultiRootRestoreJournal(
                "transaction",
                "game",
                "snapshot",
                MultiRootRestoreState.TargetsApplied,
                [
                    new RestoreRootJournalItem("a", targetA, stagingA, safetyA, RestoreRootState.Applied, true, true, true),
                    new RestoreRootJournalItem("b", targetB, stagingB, safetyB, RestoreRootState.OriginalMoved, true, true, false)
                ],
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(journalPath, JsonSerializer.Serialize(journal));

            var hashService = new FileHashService();
            var service = new SafeRestoreService(
                new GameSaveApiClient(new HttpClient()),
                new ContentObjectCache(hashService),
                hashService,
                new SqliteSyncStateStore(new SqliteDatabase(Path.Combine(root, "state.db"))));
            IReadOnlyList<string> messages = await service.RecoverInterruptedRestoresAsync(root, CancellationToken.None);
            Ensure(messages.Count == 1, "多根恢复事务未被处理。");
            Ensure(File.Exists(Path.Combine(targetA, "old.txt")), "已应用的目标目录未恢复安全备份。");
            Ensure(!File.Exists(Path.Combine(targetA, "new.txt")), "恢复过程中创建的新目录未删除。");
            Ensure(File.Exists(Path.Combine(targetB, "old.txt")), "只移动原目录的根目录未恢复。");

            MultiRootRestoreJournal? recovered = JsonSerializer.Deserialize<MultiRootRestoreJournal>(await File.ReadAllTextAsync(journalPath), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            Ensure(recovered?.State == MultiRootRestoreState.RolledBack, $"恢复完成后 Journal 未标记为 RolledBack，实际状态: {recovered?.State}。");
            Ensure(recovered is not null && recovered.Roots.All(item => item.State == RestoreRootState.RolledBack), "恢复完成后根目录状态未持久化。");
            IReadOnlyList<string> repeated = await service.RecoverInterruptedRestoresAsync(root, CancellationToken.None);
            Ensure(repeated.Count == 0, "已回滚事务被重复处理。");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}