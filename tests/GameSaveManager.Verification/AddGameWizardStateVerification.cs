using GameSaveManager.Application.Api;
using GameSaveManager.Application.Discovery;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.Verification;

internal static class AddGameWizardStateVerification
{
    public static void VerifySelectionIsolationAndSaveNavigation()
    {
        MainViewModel viewModel = SmokeViewModelFactory.Create();
        var existingGame = new CloudGame("existing-game", "旧游戏", "CUSTOM", null);
        viewModel.SelectedGame = existingGame;
        viewModel.SelectedDiscoveredGame = new DiscoveredGame(new GameIdentity(
            "旧游戏", "CUSTOM", null, @"C:\Games\Existing", @"C:\Games\Existing\existing.exe", "existing"));
        viewModel.SaveDirectory = @"C:\Saves\Existing";

        viewModel.BeginAddGameWizard();
        Ensure(string.IsNullOrEmpty(viewModel.AutoSnapshotExecutablePath), "打开新向导时必须清空上一次启动入口。");
        Ensure(viewModel.SaveLocationCandidates.Count == 0, "打开新向导时必须清空上一次存档候选。");

        var first = new DiscoveredGame(new GameIdentity(
            "第一个游戏", "STEAM", "1", @"C:\Games\First", @"C:\Games\First\first.exe", "first"));
        var second = new DiscoveredGame(new GameIdentity(
            "第二个游戏", "STEAM", "2", @"D:\Games\Second", @"D:\Games\Second\second.exe", "second"));

        viewModel.SelectedDiscoveredGame = first;
        viewModel.SaveLocationCandidates.Add(new SaveLocationCandidate(
            @"C:\Saves\First", 100, SaveLocationSource.Manual, "旧候选", 1, 1, null, [], true));
        viewModel.SaveDirectory = @"C:\Saves\First";
        viewModel.AddGameWizard.LaunchValidated = true;

        viewModel.SelectedDiscoveredGame = second;

        Ensure(viewModel.NewGameName == "第二个游戏", "切换来源后游戏名称必须跟随当前选择。");
        Ensure(viewModel.AutoSnapshotExecutablePath == second.ExecutablePath, "切换来源后启动入口必须跟随当前选择。");
        Ensure(viewModel.AddGameWizard.WorkingDirectory == second.InstallDirectory, "工作目录必须跟随当前选择。");
        Ensure(viewModel.AddGameWizard.MonitoredProcessName == second.ProcessName, "监控进程必须跟随当前选择。");
        Ensure(!viewModel.AddGameWizard.LaunchValidated, "切换来源后必须重新验证启动配置。");
        Ensure(viewModel.SaveLocationCandidates.Count == 0 && string.IsNullOrEmpty(viewModel.SaveDirectory),
            "切换来源后不得保留上一款游戏的存档检测结果。");

        viewModel.EndAddGameWizard(completed: false);
        Ensure(viewModel.SelectedGame == existingGame, "取消向导后必须恢复之前选中的游戏。");
        Ensure(viewModel.SaveDirectory == @"C:\Saves\Existing", "取消向导后必须恢复之前的存档配置界面状态。");

        viewModel.NavigateCommand.Execute("同步中心");
        Ensure(viewModel.CurrentPage == "游戏详情", "旧的管理存档路由必须兼容跳转到游戏详情。");
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
