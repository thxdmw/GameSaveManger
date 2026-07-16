using System.Reflection;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Sync;

namespace GameSaveManager.Verification;

internal static class GameDetailSyncStateVerification
{
    public static void VerifyPerGameSyncStateIsolation()
    {
        MainViewModel viewModel = SmokeViewModelFactory.Create();
        var first = new CloudGame("game-a", "游戏 A", "CUSTOM", null);
        var second = new CloudGame("game-b", "游戏 B", "CUSTOM", null);

        viewModel.SelectedGame = first;
        InvokePrivate(viewModel, "ApplySyncResult", first.GameId, new CloudSyncResult(
            CloudSyncStatus.Success, "同步完成", "snapshot-a", 1, 3, 1024, TimeSpan.FromSeconds(2)));
        InvokePrivate(viewModel, "SetGameSyncError", first.GameId, "游戏 A 的测试错误");

        Ensure(viewModel.SelectedGameLastSyncText.Contains("3 个文件", StringComparison.Ordinal),
            "游戏 A 应展示自己的备份摘要。");
        Ensure(viewModel.SelectedGameLastErrorText.Contains("游戏 A", StringComparison.Ordinal),
            "游戏 A 应展示自己的错误。");
        Ensure(viewModel.SelectedGameSyncProgressValue == 100,
            "游戏 A 完成备份后进度应为 100%。");

        viewModel.SelectedGame = second;

        Ensure(viewModel.SelectedGameLastSyncText == "暂无同步记录",
            "切换到游戏 B 后不得展示游戏 A 的备份摘要。");
        Ensure(string.IsNullOrEmpty(viewModel.SelectedGameLastErrorText),
            "切换到游戏 B 后不得展示游戏 A 的错误。");
        Ensure(viewModel.SelectedGameSyncProgressValue == 0,
            "切换到游戏 B 后不得展示游戏 A 的进度。");
        Ensure(viewModel.SelectedGameSyncProgressText == "等待立即备份",
            "未备份的游戏应展示独立的等待状态。");

    }

    private static void InvokePrivate(MainViewModel viewModel, string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(MainViewModel).GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"未找到待验证方法：{methodName}");
        try
        {
            method.Invoke(viewModel, arguments);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            throw exception.InnerException;
        }
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
