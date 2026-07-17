using GameSaveManager.Application.Games;
using GameSaveManager.Infrastructure.Discovery;
using Microsoft.Win32;

namespace GameSaveManager.Verification;

internal static class RegistryPreviewVerification
{
    public static async Task VerifyRealRegistryPreviewAsync()
    {
        string relativePath = $"Software\\GameSaveManager.Verification\\{Guid.NewGuid():N}";
        string fullPath = "HKCU\\" + relativePath;
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
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(relativePath, throwOnMissingSubKey: false);
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
