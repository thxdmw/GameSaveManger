using System.Reflection;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Verification;

internal static class GameSelectionConsistencyVerification
{
    public static void VerifyDeleteTargetAndTransientStateIsolation()
    {
        MainViewModel viewModel = SmokeViewModelFactory.Create();
        var first = new CloudGame("game-a", "游戏 A", "CUSTOM", null);
        var second = new CloudGame("game-b", "游戏 B", "CUSTOM", null);
        viewModel.Games.Add(first);
        viewModel.Games.Add(second);

        typeof(MainViewModel).GetProperty(nameof(MainViewModel.IsAuthenticated),
                BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(viewModel, true);
        Ensure(!viewModel.DeleteGameCommand.CanExecute(null), "删除命令必须拒绝缺少明确目标的调用。");
        Ensure(viewModel.DeleteGameCommand.CanExecute(first), "删除命令应接受仍在当前游戏库中的明确目标。");

        var device = new CloudDevice("device-a", "设备 A", null, true, null);
        viewModel.Devices.Add(device);
        Ensure(!viewModel.RevokeDeviceCommand.CanExecute(null), "撤销设备命令必须拒绝缺少明确目标的调用。");
        Ensure(viewModel.RevokeDeviceCommand.CanExecute(device), "撤销设备命令应接受仍在列表中的明确设备目标。");

        viewModel.SelectedGame = second;
        bool removedCurrentSelection = (bool)InvokePrivate(viewModel, "RemoveDeletedGameFromUi", first.GameId)!;
        Ensure(!removedCurrentSelection, "删除非当前游戏时不得报告当前选择已删除。");
        Ensure(viewModel.Games.Count == 1 && viewModel.Games[0].GameId == second.GameId,
            "客户端列表只能移除服务端实际删除的游戏 ID。");
        Ensure(viewModel.SelectedGame?.GameId == second.GameId,
            "删除其他游戏时不得改变当前选中的游戏。");

        viewModel.SaveLocationCandidates.Add(new SaveLocationCandidate(
            @"C:\Saves\B", 88, SaveLocationSource.RuntimeLearning, "测试候选", 2, 128, null, [], true));
        viewModel.SelectedSaveLocationCandidate = viewModel.SaveLocationCandidates[0];
        viewModel.AdditionalSaveRootPath = @"D:\Extra\B";
        viewModel.RegistrySaveKeyPath = @"HKEY_CURRENT_USER\Software\GameB";
        viewModel.SelectedGame = first;

        Ensure(viewModel.SaveLocationCandidates.Count == 0, "切换游戏后必须清空上一游戏的存档候选缓存。");
        Ensure(viewModel.SelectedSaveLocationCandidate is null, "切换游戏后必须清空上一游戏的候选选择。");
        Ensure(string.IsNullOrEmpty(viewModel.SaveDirectory), "切换游戏后必须清空上一游戏的存档路径输入。");
        Ensure(string.IsNullOrEmpty(viewModel.AdditionalSaveRootPath), "切换游戏后必须清空附加目录输入框。");
        Ensure(string.IsNullOrEmpty(viewModel.RegistrySaveKeyPath), "切换游戏后必须清空注册表路径输入框。");
        Ensure(viewModel.FileCount == 0 && viewModel.LogicalSizeText == "0 B",
            "切换游戏后必须清空上一游戏的预览统计。");
    }

    private static object? InvokePrivate(MainViewModel viewModel, string methodName, params object[] arguments)
    {
        MethodInfo method = typeof(MainViewModel).GetMethod(
            methodName, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"未找到待验证方法：{methodName}");
        try
        {
            return method.Invoke(viewModel, arguments);
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
