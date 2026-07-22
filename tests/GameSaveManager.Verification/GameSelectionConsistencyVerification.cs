using System.Reflection;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Launching;
using GameSaveManager.Application.Security;

namespace GameSaveManager.Verification;

internal static class GameSelectionConsistencyVerification
{
    public static void VerifyCachedProfileAndStoreProcessIsolation()
    {
        MainViewModel viewModel = SmokeViewModelFactory.Create();
        var first = new CloudGame("game-a", "游戏 A", "CUSTOM", null);
        var second = new CloudGame("game-b", "游戏 B", "CUSTOM", null);
        string serverKey = GameSaveServerIdentity.CreateStableKey(new Uri("http://localhost:8080"));
        var firstProfile = CreateProfile(serverKey, first.GameId, @"C:\Saves\A", @"C:\Games\A\a.exe", "a.exe");
        var secondProfile = CreateProfile(serverKey, second.GameId, @"D:\Saves\B", @"D:\Games\B\b.exe", "b.exe");
        Dictionary<string, LocalGameProfile> profiles = GetProfileCache(viewModel);
        profiles[first.GameId] = firstProfile;
        profiles[second.GameId] = secondProfile;

        viewModel.SelectedGame = first;
        Ensure(viewModel.SaveDirectory == firstProfile.SaveDirectory
               && viewModel.AutoSnapshotExecutablePath == firstProfile.ExecutablePath
               && viewModel.AutoSnapshotProcessName == firstProfile.ProcessName,
            "选择游戏时必须同步恢复该 gameId 自己的本机配置，不能保留上一款游戏字段。");

        viewModel.SelectedGame = second;
        Ensure(viewModel.SaveDirectory == secondProfile.SaveDirectory
               && viewModel.AutoSnapshotExecutablePath == secondProfile.ExecutablePath
               && viewModel.AutoSnapshotProcessName == secondProfile.ProcessName,
            "连续切换游戏时详情配置必须与当前 gameId 一致。");

        var oxygen = new CloudGame("oxygen", "Oxygen Not Included", GameIdentity.Steam, "457140");
        profiles[oxygen.GameId] = new LocalGameProfile(
            serverKey,
            oxygen.GameId,
            GameIdentity.Steam,
            "457140",
            @"D:\Steam\OxygenNotIncluded",
            secondProfile.SaveDirectory,
            secondProfile.ProcessName,
            secondProfile.ExecutablePath,
            SaveLocationSource.Manual,
            100,
            true,
            false,
            [SaveRootRule.CreateDefault(secondProfile.SaveDirectory, SaveLocationSource.Manual, 100, true)],
            [],
            @"D:\Steam\OxygenNotIncluded\OxygenNotIncluded.exe",
            new GameLaunchProfile(
                GameLaunchTargetType.StoreUri,
                "steam://run/457140",
                null,
                null,
                false,
                ["OxygenNotIncluded", "b"]),
            "smoke-user");
        viewModel.SelectedGame = oxygen;
        Ensure(string.IsNullOrEmpty(viewModel.SaveDirectory)
               && string.IsNullOrEmpty(viewModel.AutoSnapshotExecutablePath),
            "检测到平台游戏混入其他安装目录时不得把污染配置加载到详情页。");
        Ensure(viewModel.GetGameRuntimeStatusText(oxygen) == "启动配置异常"
               && viewModel.GetLaunchDisabledReason(oxygen) is { Length: > 0 },
            "已污染的本机配置必须同时阻止运行状态判断和游戏启动。");

        var merger = new GameLaunchProfileMerger();
        var existing = new GameLaunchProfile(
            GameLaunchTargetType.StoreUri,
            "steam://run/457140",
            null,
            null,
            false,
            ["OxygenNotIncluded"]);
        GameLaunchProfile merged = merger.Merge(
                existing,
                new GameIdentity("Oxygen Not Included", GameIdentity.Steam, "457140",
                    @"D:\Steam\OxygenNotIncluded", @"D:\Steam\OxygenNotIncluded\OxygenNotIncluded.exe",
                    "OxygenNotIncluded.exe"),
                @"D:\GameTest\LIMBO\limbo.exe",
                "limbo.exe")
            ?? throw new InvalidOperationException("平台启动配置不应在合并后消失。");
        Ensure(!merged.MonitoredProcessNames.Contains("limbo", StringComparer.OrdinalIgnoreCase),
            "平台游戏保存配置时不得把界面残留的另一款游戏进程追加到监控列表。");
    }

    public static async Task VerifySynchronizationNeverPersistsTransientUiAsync()
    {
        string root = Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "slot.sav"), "save");
            var store = new RecordingProfileStore();
            MainViewModel viewModel = SmokeViewModelFactory.Create(store, credentialStore: new FixedCredentialStore());
            const string gameId = "sync-write-guard";
            string serverKey = GameSaveServerIdentity.CreateStableKey(new Uri("http://localhost:8080"));
            LocalGameProfile profile = CreateProfile(serverKey, gameId, root, null, "correct.exe");
            GetProfileCache(viewModel)[gameId] = profile;
            viewModel.SelectedGame = new CloudGame(gameId, "同步写保护", "CUSTOM", null);

            // 模拟切页时界面进程字段被另一款游戏污染；同步必须拒绝，而不是将其保存到当前记录。
            viewModel.AutoSnapshotProcessName = "foreign-game.exe";
            Task sync = (Task)(InvokePrivate(viewModel, "SyncAsync")
                               ?? throw new InvalidOperationException("同步方法未返回任务。"));
            await sync;
            Ensure(store.SaveCount == 0,
                "立即备份只能读取已确认配置，绝不能把瞬态界面字段回写本机游戏记录。");
            Ensure(GetProfileCache(viewModel)[gameId] == profile,
                "同步被配置归属校验阻止后，内存缓存也必须保持原样。");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

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

    private static Dictionary<string, LocalGameProfile> GetProfileCache(MainViewModel viewModel) =>
        (Dictionary<string, LocalGameProfile>)(typeof(MainViewModel)
            .GetField("_localGameProfiles", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(viewModel)
            ?? throw new InvalidOperationException("无法读取本机游戏配置缓存。"));

    private static LocalGameProfile CreateProfile(
        string serverKey,
        string gameId,
        string saveDirectory,
        string? executablePath,
        string processName) => new(
        serverKey,
        gameId,
        GameIdentity.Custom,
        null,
        executablePath is null ? string.Empty : Path.GetDirectoryName(executablePath),
        saveDirectory,
        processName,
        executablePath,
        SaveLocationSource.Manual,
        100,
        true,
        false,
        [SaveRootRule.CreateDefault(saveDirectory, SaveLocationSource.Manual, 100, true)],
        [],
        executablePath,
        executablePath is null
            ? null
            : new GameLaunchProfile(
                GameLaunchTargetType.Executable,
                executablePath,
                null,
                Path.GetDirectoryName(executablePath),
                false,
                [processName]),
        "smoke-user");

    private sealed class RecordingProfileStore : ILocalGameProfileStore
    {
        public int SaveCount { get; private set; }

        public Task<LocalGameProfile?> GetAsync(
            string serverKey, string userId, string gameId, CancellationToken cancellationToken) =>
            Task.FromResult<LocalGameProfile?>(null);

        public Task SaveAsync(LocalGameProfile profile, CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<LocalGameProfile>> ListAsync(
            string serverKey, string userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LocalGameProfile>>([]);

        public Task ClaimLegacyAsync(
            string serverKey, string userId, IReadOnlyCollection<string> ownedGameIds,
            CancellationToken cancellationToken) => Task.CompletedTask;

        public Task DeleteAsync(
            string serverKey, string userId, string gameId, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FixedCredentialStore : ICredentialStore
    {
        public Task SaveAsync(string target, string secret, CancellationToken cancellationToken) =>
            Task.CompletedTask;

        public Task<string?> ReadAsync(string target, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("verification-token");

        public Task DeleteAsync(string target, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
