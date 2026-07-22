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

        Ensure(!viewModel.SelectGameCommand.CanExecute(first),
            "已经不在当前账号游戏库中的旧对象不得重新成为选择目标。");
        Ensure(viewModel.SelectGameCommand.CanExecute(second),
            "当前账号游戏库中的有效对象应允许选择。");

        viewModel.NewGameName = "未提交草稿";
        viewModel.AdditionalSaveRootPath = @"D:\Draft";
        viewModel.RegistrySaveKeyPath = @"HKEY_CURRENT_USER\Software\Draft";
        viewModel.AutoSnapshotProcessName = "draft.exe";
        InvokePrivate(viewModel, "ClearAuthenticatedUiState");
        Ensure(viewModel.Games.Count == 0 && viewModel.SelectedGame is null,
            "退出账号后必须同时清空游戏列表和当前选择。");
        Ensure(string.IsNullOrEmpty(viewModel.NewGameName)
               && string.IsNullOrEmpty(viewModel.SaveDirectory)
               && string.IsNullOrEmpty(viewModel.AdditionalSaveRootPath)
               && string.IsNullOrEmpty(viewModel.RegistrySaveKeyPath)
               && string.IsNullOrEmpty(viewModel.AutoSnapshotProcessName)
               && viewModel.SaveLocationCandidates.Count == 0,
            "退出账号后必须清空游戏草稿、路径输入和候选缓存，不能带入下一个账号。");
        Ensure(!viewModel.SelectGameCommand.CanExecute(second),
            "退出账号后必须拒绝旧账号残留列表项触发选择。");
        Ensure(viewModel.LoginCommand.CanExecute(null) && viewModel.RegisterCommand.CanExecute(null),
            "退出账号后应恢复登录和注册入口。");
        typeof(MainViewModel).GetField("_authenticationInProgress",
                BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(viewModel, 1);
        Ensure(!viewModel.LoginCommand.CanExecute(null) && !viewModel.RegisterCommand.CanExecute(null),
            "登录或注册进行期间必须同时禁用两个入口，防止交叉连点切换会话。");
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
