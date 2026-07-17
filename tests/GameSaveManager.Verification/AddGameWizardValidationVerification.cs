using System.Reflection;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Discovery;

namespace GameSaveManager.Verification;

internal static class AddGameWizardValidationVerification
{
    public static async Task VerifyStepGatesAndAggregatePreviewAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "GameSaveManager.Wizard", Guid.NewGuid().ToString("N"));
        string gameDirectory = Path.Combine(root, "game");
        string primary = Path.Combine(root, "primary");
        string additional = Path.Combine(root, "additional");
        Directory.CreateDirectory(gameDirectory);
        Directory.CreateDirectory(primary);
        Directory.CreateDirectory(additional);
        string executable = Path.Combine(gameDirectory, "game.exe");
        await File.WriteAllBytesAsync(executable, [0x4d, 0x5a]);
        await File.WriteAllTextAsync(Path.Combine(primary, "a.sav"), "a");
        await File.WriteAllTextAsync(Path.Combine(additional, "b.sav"), "b");
        try
        {
            MainViewModel viewModel = SmokeViewModelFactory.Create();
            AddGameWizardViewModel wizard = viewModel.AddGameWizard;
            viewModel.BeginAddGameWizard();
            Ensure(!wizard.CanMoveNext, "未选择来源时不得离开第一步。");

            viewModel.SelectedDiscoveredGame = new DiscoveredGame(new GameIdentity(
                "验证游戏", GameIdentity.Local, null, gameDirectory, executable, "launcher"));
            Ensure(wizard.TryMoveNext() && wizard.Step == 2, "有效来源应允许进入启动配置。");

            wizard.SetDetectedProcesses(["launcher", "real-game"]);
            wizard.LaunchValidated = true;
            wizard.Arguments = "--changed";
            Ensure(!wizard.LaunchValidated && wizard.DetectedProcessOptions.Count == 0,
                "启动参数变化必须使测试结果和进程候选失效。");
            wizard.SetDetectedProcesses(["launcher", "real-game"]);
            wizard.LaunchValidated = true;
            Ensure(wizard.GetConfirmedMonitoredProcessNames().Count == 2,
                "测试发现的多个真实进程必须保留到最终启动配置。" );
            Ensure(wizard.TryMoveNext() && wizard.Step == 3, "有效启动目标应允许进入存档检测。");

            viewModel.SaveDirectory = primary;
            Ensure(wizard.TryMoveNext() && wizard.Step == 4, "选择存在的主目录后应进入预览确认。");
            viewModel.AdditionalSaveRootPath = additional;
            await InvokePrivateAsync(viewModel, "AddAdditionalSaveRootAsync");
            Ensure(viewModel.AdditionalSaveRoots.Single().UserConfirmed == false,
                "附加目录在完整预览前不得直接标记为已确认。" );

            await InvokePrivateAsync(viewModel, "PreviewSaveDirectoryAsync");
            Ensure(viewModel.FileCount == 2 && viewModel.SaveRootPreviews.Count == 2,
                "聚合预览必须扫描主目录和全部附加目录。" );
            await InvokePrivateAsync(viewModel, "ConfirmSaveDirectoryAsync");
            Ensure(viewModel.IsSaveDirectoryConfirmed && viewModel.AdditionalSaveRoots.All(item => item.UserConfirmed),
                "完整预览后应逐根确认全部目录。" );
            Ensure(wizard.TryMoveNext() && wizard.Step == 5, "确认完整配置后应进入保护方式。");

            wizard.EnableAutomaticBackup = true;
            wizard.LaunchValidated = false;
            Ensure(!wizard.CanMoveNext, "自动备份开启时不得跳过启动验证。" );
            wizard.SetDetectedProcesses(["real-game"]);
            wizard.LaunchValidated = true;
            Ensure(wizard.TryMoveNext() && wizard.Step == 6 && wizard.IsFinalConfigurationValid,
                "全部条件满足后才应允许最终提交。" );
            viewModel.EndAddGameWizard(completed: false);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch (IOException) { }
        }
    }

    private static async Task InvokePrivateAsync(MainViewModel viewModel, string methodName)
    {
        MethodInfo method = typeof(MainViewModel).GetMethod(methodName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"未找到待验证方法：{methodName}");
        try
        {
            await (Task)(method.Invoke(viewModel, null)
                ?? throw new InvalidOperationException($"方法未返回任务：{methodName}"));
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
