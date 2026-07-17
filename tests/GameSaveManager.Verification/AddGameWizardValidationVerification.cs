using System.Reflection;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Files;
using GameSaveManager.Application.Games;
using GameSaveManager.Infrastructure.FileSystem;

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

            wizard.WorkingDirectory = Path.Combine(root, "missing-working-directory");
            Ensure(!wizard.CanMoveNext,
                "即使只使用手动备份，不存在的工作目录也不得通过启动配置校验。");
            wizard.WorkingDirectory = gameDirectory;

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

            SaveRootRule primaryRule = SaveRootRule.CreateDefault(
                primary, SaveLocationSource.Manual, 100, false);
            SaveRootRule nestedRule = new("root2", Path.Combine(primary, "nested"), [], [],
                SaveLocationSource.Manual, 100, false);
            Directory.CreateDirectory(nestedRule.Path);
            ExpectThrows<InvalidOperationException>(() =>
                SaveRootTopologyValidator.Validate([primaryRule, nestedRule]));
            string driveRoot = Path.GetPathRoot(root)
                ?? throw new InvalidOperationException("测试目录缺少磁盘根路径。");
            ExpectThrows<InvalidOperationException>(() => SaveRootTopologyValidator.Validate([
                SaveRootRule.CreateDefault(driveRoot, SaveLocationSource.Manual, 100, false)
            ]));
            foreach (DriveInfo drive in DriveInfo.GetDrives().Where(item => item.IsReady))
            {
                ExpectThrows<InvalidOperationException>(() => SaveRootTopologyValidator.Validate([
                    SaveRootRule.CreateDefault(drive.RootDirectory.FullName,
                        SaveLocationSource.Manual, 100, false)
                ]));
            }
            ExpectThrows<InvalidOperationException>(() => SaveRootTopologyValidator.Validate([
                SaveRootRule.CreateDefault(@"\\server\share\", SaveLocationSource.Manual, 100, false)
            ]));

            string overflow = Path.Combine(root, "overflow");
            Directory.CreateDirectory(overflow);
            for (int index = 0; index < 5002; index++)
                await File.WriteAllTextAsync(Path.Combine(overflow, $"{index}.sav"), string.Empty);
            IReadOnlyList<ScannedSaveFile> capped = await new SaveDirectoryScanner().ScanAsync(
                SaveRootRule.CreateDefault(overflow, SaveLocationSource.Manual, 100, false),
                CancellationToken.None);
            Ensure(capped.Count == 5001, "目录扫描达到协议上限加一后必须提前终止。");

            SaveDirectoryScanResult excludedBudget = await new SaveDirectoryScanner().ScanWithBudgetAsync(
                new SaveRootRule("excluded", overflow, ["**/*.never"], [],
                    SaveLocationSource.Manual, 100, false),
                new SaveDirectoryScanBudget(100, 10, 100, TimeSpan.FromSeconds(10)),
                CancellationToken.None);
            Ensure(excludedBudget.WasTruncated && excludedBudget.Files.Count == 0
                && excludedBudget.VisitedFileCount == 10,
                "大量被排除文件也必须受独立访问文件预算限制。");

            MainViewModel failingViewModel = SmokeViewModelFactory.Create(new FailingProfileStore());
            failingViewModel.SelectedGame = new GameSaveManager.Application.Api.CloudGame(
                "persist-failure", "持久化失败验证", "CUSTOM", null);
            failingViewModel.SaveDirectory = primary;
            await InvokePrivateAsync(failingViewModel, "PreviewSaveDirectoryAsync");
            await InvokePrivateAsync(failingViewModel, "ConfirmSaveDirectoryAsync");
            Ensure(!failingViewModel.IsSaveDirectoryConfirmed,
                "本地 Profile 持久化失败后 UI 不得提前显示已确认。");

            var recordingStore = new RecordingProfileStore();
            MainViewModel manualOnlyViewModel = SmokeViewModelFactory.Create(recordingStore);
            manualOnlyViewModel.SelectedGame = new GameSaveManager.Application.Api.CloudGame(
                "manual-only", "仅手动备份验证", "CUSTOM", null);
            manualOnlyViewModel.SaveDirectory = primary;
            await InvokePrivateAsync(manualOnlyViewModel, "PreviewSaveDirectoryAsync");
            await InvokePrivateAsync(manualOnlyViewModel, "ConfirmSaveDirectoryAsync");
            Ensure(manualOnlyViewModel.IsSaveDirectoryConfirmed
                && recordingStore.SavedProfile is { EffectiveLaunchProfile: null, AutoSnapshotEnabled: false },
                "旧云端游戏应允许只保存手动备份目录，并保持启动与自动备份禁用。");
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

    private static void ExpectThrows<TException>(Action action) where TException : Exception
    {
        try { action(); }
        catch (TException) { return; }
        throw new InvalidOperationException($"预期抛出 {typeof(TException).Name}。");
    }

    private sealed class FailingProfileStore : ILocalGameProfileStore
    {
        public Task<LocalGameProfile?> GetAsync(string serverKey, string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<LocalGameProfile?>(null);

        public Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken) =>
            Task.FromException(new IOException("模拟 SQLite 持久化失败。"));

        public Task<IReadOnlyList<LocalGameProfile>> ListAsync(
            string serverKey, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LocalGameProfile>>([]);

        public Task DeleteAsync(string serverKey, string gameId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class RecordingProfileStore : ILocalGameProfileStore
    {
        public LocalGameProfile? SavedProfile { get; private set; }

        public Task<LocalGameProfile?> GetAsync(string serverKey, string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<LocalGameProfile?>(null);

        public Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken)
        {
            SavedProfile = profile;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LocalGameProfile>> ListAsync(
            string serverKey, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LocalGameProfile>>([]);

        public Task DeleteAsync(string serverKey, string gameId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }
}
