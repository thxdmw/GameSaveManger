using GameSaveManager.Application.Games;
using GameSaveManager.Application.Restores;
using GameSaveManager.Infrastructure.Discovery;
using Microsoft.Win32;

namespace GameSaveManager.Verification;

internal static class RegistryPreviewVerification
{
    public static async Task VerifyRealRegistryPreviewAsync()
    {
        string relativePath = $"Software\\GameSaveManager.Verification\\{Guid.NewGuid():N}";
        string fullPath = "HKCU\\" + relativePath;
        string snapshotDirectory = Path.Combine(Path.GetTempPath(), "GameSaveManager.RegistrySnapshot", Guid.NewGuid().ToString("N"));
        string transactionDirectory = Path.Combine(Path.GetTempPath(), "GameSaveManager.RegistryTransaction", Guid.NewGuid().ToString("N"));
        try
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(relativePath, writable: true)
                ?? throw new InvalidOperationException("无法创建验证注册表键。"))
            {
                key.SetValue("Name", "Player", RegistryValueKind.String);
                key.SetValue("Score", 42, RegistryValueKind.DWord);
            }

            var service = new WindowsRegistrySaveSnapshotService();
            RegistrySavePreview preview = (await service.PreviewAsync(
                [new RegistrySaveRule("registry1", fullPath, false)], CancellationToken.None)).Single();
            Ensure(preview.KeyExists && preview.IsReadable && preview.CanConfirm,
                "存在且仅含受支持类型的 HKCU 键应允许确认。");
            Ensure(preview.ValueCount == 2 && preview.EstimatedSize > 0,
                "注册表预览必须报告值数量和预计导出大小。");

            RegistrySavePreview missing = (await service.PreviewAsync(
                [new RegistrySaveRule("registry2", fullPath + "\\Missing", false)], CancellationToken.None)).Single();
            Ensure(!missing.KeyExists && !missing.CanConfirm,
                "不存在的注册表键不得被确认。" );

            string deepPath = relativePath + "\\" + string.Join("\\",
                Enumerable.Range(1, 34).Select(index => $"Level{index}"));
            using (RegistryKey deep = Registry.CurrentUser.CreateSubKey(deepPath, writable: true)
                ?? throw new InvalidOperationException("无法创建深层验证注册表键。"))
                deep.SetValue("Value", "deep");
            RegistrySavePreview tooDeep = (await service.PreviewAsync(
                [new RegistrySaveRule("registry3", fullPath, false)], CancellationToken.None)).Single();
            Ensure(!tooDeep.CanConfirm && tooDeep.Error?.Contains("32", StringComparison.Ordinal) == true,
                "超过深度预算的注册表规则必须停止预览且不得确认。");
            Registry.CurrentUser.DeleteSubKeyTree(relativePath + "\\Level1", throwOnMissingSubKey: false);

            string secondRelativePath = relativePath + "\\Second";
            string secondFullPath = "HKCU\\" + secondRelativePath;
            using (RegistryKey second = Registry.CurrentUser.CreateSubKey(secondRelativePath, writable: true)
                ?? throw new InvalidOperationException("无法创建第二个验证注册表键。"))
                second.SetValue("Name", "snapshot-second", RegistryValueKind.String);
            var confirmedRules = new[]
            {
                new RegistrySaveRule("registry1", fullPath, true),
                new RegistrySaveRule("registry2", secondFullPath, true)
            };
            Directory.CreateDirectory(snapshotDirectory);
            Directory.CreateDirectory(transactionDirectory);
            await service.ExportAsync(snapshotDirectory, confirmedRules, CancellationToken.None);
            using (RegistryKey first = Registry.CurrentUser.OpenSubKey(relativePath, writable: true)!)
                first.SetValue("Name", "current-first", RegistryValueKind.String);
            using (RegistryKey second = Registry.CurrentUser.OpenSubKey(secondRelativePath, writable: true)!)
                second.SetValue("Name", "current-second", RegistryValueKind.String);

            IRegistryRestoreTransaction restore = service;
            RegistryRestorePreparation preparation = await restore.PrepareAsync(
                snapshotDirectory, confirmedRules, transactionDirectory, CancellationToken.None);
            string secondDocument = Path.Combine(snapshotDirectory, "registry2.json");
            string corrupt = await File.ReadAllTextAsync(secondDocument);
            await File.WriteAllTextAsync(secondDocument,
                corrupt.Replace("\"Kind\": \"String\"", "\"Kind\": \"Unknown\"", StringComparison.Ordinal));
            await ExpectThrowsAsync<InvalidDataException>(() => restore.ApplyAsync(preparation, CancellationToken.None));
            using RegistryKey unchanged = Registry.CurrentUser.OpenSubKey(relativePath, writable: false)
                ?? throw new InvalidOperationException("验证注册表键意外消失。");
            Ensure(string.Equals(unchanged.GetValue("Name")?.ToString(), "current-first", StringComparison.Ordinal),
                "任一恢复文档无效时，必须在删除任何现有注册表键之前整体失败。");

            string oversizedDirectory = Path.Combine(snapshotDirectory, "oversized");
            Directory.CreateDirectory(oversizedDirectory);
            string oversizedDocument = Path.Combine(oversizedDirectory, "registry1.json");
            await using (FileStream oversized = new(oversizedDocument, FileMode.Create, FileAccess.Write))
                oversized.SetLength(64L * 1024 * 1024 + 1);
            await ExpectThrowsAsync<InvalidDataException>(() => restore.PrepareAsync(
                oversizedDirectory, [confirmedRules[0]], transactionDirectory, CancellationToken.None));
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
            try { Directory.Delete(snapshotDirectory, recursive: true); } catch (IOException) { }
            try { Directory.Delete(transactionDirectory, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task ExpectThrowsAsync<TException>(Func<Task> action) where TException : Exception
    {
        try { await action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
