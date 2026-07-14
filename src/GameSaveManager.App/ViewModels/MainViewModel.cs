using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using GameSaveManager.App.Common;
using GameSaveManager.App.Theming;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Device;
using GameSaveManager.Application.Diagnostics;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Monitoring;
using GameSaveManager.Application.Restores;
using GameSaveManager.Application.Security;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Sync;
using GameSaveManager.Application.Startup;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.App.ViewModels;

/// <summary>主窗口交互编排；业务 I/O 均通过 Application 服务执行。</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SaveManifestBuilder _manifestBuilder;
    private readonly IGameSaveApiClient _apiClient;
    private readonly CloudSyncService _cloudSyncService;
    private readonly SafeRestoreService _safeRestoreService;
    private readonly IAutoSyncCoordinator _autoSyncCoordinator;
    private readonly SnapshotExportService _snapshotExportService;
    private readonly SemaphoreSlim _syncQueue = new(1, 1);
    private CancellationTokenSource? _syncCancellation;
    private readonly IGameDiscoveryService _gameDiscoveryService;
    private readonly ISavePathSuggestionService _savePathSuggestionService;
    private readonly ILocalGameProfileStore _localGameProfileStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;
    private readonly IAppLogger _appLogger;
    private readonly IAutoStartService _autoStartService;

    private string _serverAddress = "http://localhost:8080";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _newGameName = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _autoSnapshotProcessName = string.Empty;
    private string _statusText = "请先注册或登录，然后选择云端游戏并配置本地存档目录。";
    private int _fileCount;
    private string _logicalSizeText = "0 B";
    private string _currentPage = "首页";
    private string _gameSearchText = string.Empty;
    private bool _isAuthenticated;
    private string _authenticatedUsername = string.Empty;
    private bool _isAutoSyncEnabled;
    private bool _isSyncing;
    private string _syncProgressText = "等待同步";
    private double _syncProgressValue;
    private string _syncSummaryText = "暂无同步记录";
    private string _restorePreviewText = "选择快照后可预览将恢复的文件数量与大小。";
    private bool _isLightTheme;
    private bool _autoStartEnabled;
    private string _quotaUsageText = "尚未加载存储容量";
    private bool _retentionEnabled;
    private string _retentionCountText = "50";
    private string _retentionDaysText = "0";
    private CloudGame? _selectedGame;
    private CloudSnapshotSummary? _selectedSnapshot;
    private DiscoveredGame? _selectedDiscoveredGame;
    private string? _selectedSaveDirectorySuggestion;
    private CloudDevice? _selectedDevice;

    public MainViewModel(
        SaveManifestBuilder manifestBuilder,
        IGameSaveApiClient apiClient,
        CloudSyncService cloudSyncService,
        SafeRestoreService safeRestoreService,
        IAutoSyncCoordinator autoSyncCoordinator,
        SnapshotExportService snapshotExportService,
        IGameDiscoveryService gameDiscoveryService,
        ISavePathSuggestionService savePathSuggestionService,
        ILocalGameProfileStore localGameProfileStore,
        ICredentialStore credentialStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        IAppLogger appLogger,
        IAutoStartService autoStartService)
    {
        _manifestBuilder = manifestBuilder;
        _apiClient = apiClient;
        _cloudSyncService = cloudSyncService;
        _safeRestoreService = safeRestoreService;
        _autoSyncCoordinator = autoSyncCoordinator;
        _snapshotExportService = snapshotExportService;
        _gameDiscoveryService = gameDiscoveryService;
        _savePathSuggestionService = savePathSuggestionService;
        _localGameProfileStore = localGameProfileStore;
        _credentialStore = credentialStore;
        _deviceIdentityProvider = deviceIdentityProvider;
        _appLogger = appLogger;
        _autoStartService = autoStartService;
        _autoStartEnabled = autoStartService.IsEnabled();

        RegisterCommand = new AsyncCommand(() => AuthenticateAsync(true));
        LoginCommand = new AsyncCommand(() => AuthenticateAsync(false));
        CreateGameCommand = new AsyncCommand(CreateGameAsync);
        DeleteGameCommand = new AsyncCommand(DeleteGameAsync);
        LogoutCommand = new AsyncCommand(LogoutAsync);
        AccountActionCommand = new AsyncCommand(AccountActionAsync);
        BuildManifestCommand = new AsyncCommand(BuildManifestAsync);
        SyncCommand = new AsyncCommand(SyncAsync);
        RetrySyncCommand = new AsyncCommand(SyncAsync);
        CancelSyncCommand = new DelegateCommand(_ => _syncCancellation?.Cancel());
        ReloadSnapshotsCommand = new AsyncCommand(ReloadSnapshotsFromUiAsync);
        DeleteSnapshotCommand = new AsyncCommand(DeleteSnapshotAsync);
        RestoreCommand = new AsyncCommand(RestoreAsync);
        LoadRestorePreviewCommand = new AsyncCommand(LoadRestorePreviewAsync);
        ExportSnapshotCommand = new AsyncCommand(ExportSnapshotAsync);
        StartAutoSnapshotCommand = new AsyncCommand(StartAutoSnapshotAsync);
        StopAutoSnapshotCommand = new AsyncCommand(StopAutoSnapshotAsync);
        DiscoverGamesCommand = new AsyncCommand(DiscoverGamesAsync);
        SuggestSaveDirectoriesCommand = new AsyncCommand(SuggestSaveDirectoriesAsync);
        LoadLocalProfileCommand = new AsyncCommand(LoadLocalProfileFromUiAsync);
        ReloadDevicesCommand = new AsyncCommand(ReloadDevicesAsync);
        ReloadQuotaCommand = new AsyncCommand(ReloadQuotaAsync);
        ReloadRetentionCommand = new AsyncCommand(ReloadRetentionAsync);
        SaveRetentionCommand = new AsyncCommand(SaveRetentionAsync);
        CleanupRetentionCommand = new AsyncCommand(CleanupRetentionAsync);
        RevokeDeviceCommand = new AsyncCommand(RevokeDeviceAsync);
        KeepLocalConflictCommand = new AsyncCommand(KeepLocalConflictAsync);
        NavigateCommand = new DelegateCommand(NavigateTo);
        SelectGameCommand = new AsyncCommand(SelectGameAsync);
        ToggleThemeCommand = new DelegateCommand(_ => ToggleTheme());
        ToggleAutoStartCommand = new AsyncCommand(ToggleAutoStartAsync);
        FilteredGames = CollectionViewSource.GetDefaultView(Games);
        FilteredGames.Filter = MatchesGameSearch;
        Games.CollectionChanged += (_, _) => { FilteredGames.Refresh(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasGames))); };
    }

    public ObservableCollection<CloudGame> Games { get; } = [];
    public ObservableCollection<CloudSnapshotSummary> Snapshots { get; } = [];
    public ObservableCollection<DiscoveredGame> DiscoveredGames { get; } = [];
    public ObservableCollection<string> SaveDirectorySuggestions { get; } = [];
    public ObservableCollection<CloudDevice> Devices { get; } = [];
    public ICollectionView FilteredGames { get; }
    public bool HasGames => Games.Count > 0;

    public string ServerAddress { get => _serverAddress; set => SetField(ref _serverAddress, value); }
    public string Username { get => _username; set => SetField(ref _username, value); }
    public string NewGameName { get => _newGameName; set => SetField(ref _newGameName, value); }
    public string SaveDirectory { get => _saveDirectory; set => SetField(ref _saveDirectory, value); }
    public string AutoSnapshotProcessName { get => _autoSnapshotProcessName; set => SetField(ref _autoSnapshotProcessName, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public int FileCount { get => _fileCount; private set => SetField(ref _fileCount, value); }
    public string LogicalSizeText { get => _logicalSizeText; private set => SetField(ref _logicalSizeText, value); }
    public string CurrentPage { get => _currentPage; private set => SetField(ref _currentPage, value); }
    public string GameSearchText
    {
        get => _gameSearchText;
        set
        {
            if (SetField(ref _gameSearchText, value)) FilteredGames.Refresh();
        }
    }
    public string QuotaUsageText { get => _quotaUsageText; private set => SetField(ref _quotaUsageText, value); }
    public bool RetentionEnabled { get => _retentionEnabled; set => SetField(ref _retentionEnabled, value); }
    public bool IsAuthenticated
    {
        get => _isAuthenticated;
        private set
        {
            if (!SetField(ref _isAuthenticated, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ConnectionStatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CloudReadinessText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountActionText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AccountSummaryText)));
        }
    }
    public bool AutoStartEnabled { get => _autoStartEnabled; private set => SetField(ref _autoStartEnabled, value); }
    public string AutoStartText => AutoStartEnabled ? "已启用开机启动" : "启用开机启动";
    public bool IsLightTheme
    {
        get => _isLightTheme;
        private set
        {
            if (!SetField(ref _isLightTheme, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeToggleText)));
        }
    }
    public string AuthenticatedUsername { get => _authenticatedUsername; private set => SetField(ref _authenticatedUsername, value); }
    public bool IsAutoSyncEnabled { get => _isAutoSyncEnabled; private set => SetField(ref _isAutoSyncEnabled, value); }
    public bool IsSyncing { get => _isSyncing; private set => SetField(ref _isSyncing, value); }
    public string SyncProgressText { get => _syncProgressText; private set => SetField(ref _syncProgressText, value); }
    public double SyncProgressValue { get => _syncProgressValue; private set => SetField(ref _syncProgressValue, value); }
    public string SyncSummaryText { get => _syncSummaryText; private set => SetField(ref _syncSummaryText, value); }
    public string RestorePreviewText { get => _restorePreviewText; private set => SetField(ref _restorePreviewText, value); }
    public string ConnectionStatusText => IsAuthenticated ? "已登录" : "未登录";
    public string AccountActionText => IsAuthenticated ? "退出登录" : "登录";
    public string AccountSummaryText => IsAuthenticated ? $"当前账号：{AuthenticatedUsername}" : "尚未登录";
    public string CloudReadinessText => IsAuthenticated ? "云端同步已就绪" : "登录后启用云端同步";
    public string ThemeToggleText => IsLightTheme ? "切换至深色主题" : "切换至浅色主题";
    public string RetentionCountText { get => _retentionCountText; set => SetField(ref _retentionCountText, value); }
    public string RetentionDaysText { get => _retentionDaysText; set => SetField(ref _retentionDaysText, value); }

    public CloudGame? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetField(ref _selectedGame, value))
            {
                Snapshots.Clear();
                SelectedSnapshot = null;
            }
        }
    }

    public CloudSnapshotSummary? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set => SetField(ref _selectedSnapshot, value);
    }

    public CloudDevice? SelectedDevice
    {
        get => _selectedDevice;
        set => SetField(ref _selectedDevice, value);
    }
    public string? SelectedSaveDirectorySuggestion
    {
        get => _selectedSaveDirectorySuggestion;
        set
        {
            if (SetField(ref _selectedSaveDirectorySuggestion, value) && !string.IsNullOrWhiteSpace(value)) SaveDirectory = value;
        }
    }

    public DiscoveredGame? SelectedDiscoveredGame
    {
        get => _selectedDiscoveredGame;
        set
        {
            if (SetField(ref _selectedDiscoveredGame, value) && value is not null)
            {
                NewGameName = value.Name;
                if (!string.IsNullOrWhiteSpace(value.ProcessName)) AutoSnapshotProcessName = value.ProcessName;
            }
        }
    }

    public ICommand RegisterCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand CreateGameCommand { get; }
    public ICommand DeleteGameCommand { get; }
    public ICommand LogoutCommand { get; }
    public ICommand AccountActionCommand { get; }
    public ICommand BuildManifestCommand { get; }
    public ICommand SyncCommand { get; }
    public ICommand RetrySyncCommand { get; }
    public ICommand CancelSyncCommand { get; }
    public ICommand ReloadSnapshotsCommand { get; }
    public ICommand DeleteSnapshotCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand LoadRestorePreviewCommand { get; }
    public ICommand ExportSnapshotCommand { get; }
    public ICommand StartAutoSnapshotCommand { get; }
    public ICommand StopAutoSnapshotCommand { get; }
    public ICommand DiscoverGamesCommand { get; }
    public ICommand SuggestSaveDirectoriesCommand { get; }
    public ICommand LoadLocalProfileCommand { get; }
    public ICommand ReloadDevicesCommand { get; }
    public ICommand ReloadQuotaCommand { get; }
    public ICommand ReloadRetentionCommand { get; }
    public ICommand SaveRetentionCommand { get; }
    public ICommand CleanupRetentionCommand { get; }
    public ICommand RevokeDeviceCommand { get; }
    public ICommand KeepLocalConflictCommand { get; }
    public ICommand NavigateCommand { get; }
    public ICommand SelectGameCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleAutoStartCommand { get; }

    public event EventHandler? PasswordClearRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>密码仅暂存于内存，并由 PasswordBox 调用此方法传入。</summary>
    public void SetPassword(string password) => _password = password;


    /// <summary>启动时尝试恢复已保存的设备会话；失效 Token 只清理凭据，不影响本机文件。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            Uri server = ParseServerUri();
            string? token = await _credentialStore.ReadAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
            if (string.IsNullOrWhiteSpace(token)) return;

            AuthenticatedUsername = await _credentialStore.ReadAsync(CredentialTargets.ForAccountName(server), CancellationToken.None) ?? "已登录账号";
            IsAuthenticated = true;
            await ReloadGamesAsync(server, token);
            await ReloadDevicesAsync(server, token);
            await ReloadQuotaAsync(server, token);
            StatusText = $"已恢复账号 {AuthenticatedUsername} 的登录状态。";
        }
        catch (GameSaveApiException exception) when (exception.StatusCode is 401 or 403)
        {
            Uri server = ParseServerUri();
            await _credentialStore.DeleteAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
            await _credentialStore.DeleteAsync(CredentialTargets.ForAccountName(server), CancellationToken.None);
            IsAuthenticated = false;
            AuthenticatedUsername = string.Empty;
            StatusText = "登录状态已过期，请重新登录。";
        }
        catch (Exception exception)
        {
            _appLogger.Error("application.session_restore.failed", exception, "恢复本机登录会话失败");
            IsAuthenticated = false;
            StatusText = "无法恢复登录状态；请检查服务端地址或重新登录。";
        }
    }
    /// <summary>切换导航页面；每个页面复用同一份同步与本地配置状态。</summary>
    private void NavigateTo(object? page)
    {
        string target = page?.ToString() ?? "首页";
        CurrentPage = target;
        StatusText = target == "首页" ? "已返回同步概览。" : $"已切换到{target}。";
    }

    private void ToggleTheme()
    {
        IsLightTheme = !IsLightTheme;
        ThemeManager.Apply(IsLightTheme);
        StatusText = IsLightTheme ? "已启用浅色主题。" : "已启用深色主题。";
    }

    private async Task ToggleAutoStartAsync()
    {
        try
        {
            bool next = !AutoStartEnabled;
            await _autoStartService.SetEnabledAsync(next, CancellationToken.None);
            AutoStartEnabled = next;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoStartText)));
            StatusText = next ? "已启用当前用户的开机启动。" : "已关闭开机启动。";
        }
        catch (Exception exception) { ShowError("修改开机启动失败", exception); }
    }
    /// <summary>首页/游戏库选择游戏时只切换当前展示，并加载该游戏自己的时间线。</summary>
    private async Task SelectGameAsync(object? game)
    {
        if (game is not CloudGame selected) return;
        SelectedGame = selected;
        try
        {
            Uri server = ParseServerUri();
            await ReloadSnapshotsAsync(server, await RequireDeviceTokenAsync(server));
            StatusText = $"已选择{selected.Name}。可在同步中心配置存档路径和同步。";
        }
        catch (Exception exception) { ShowError("加载游戏快照失败", exception); }
    }

    private bool MatchesGameSearch(object candidate)
    {
        if (candidate is not CloudGame game) return false;
        return string.IsNullOrWhiteSpace(GameSearchText)
            || game.Name.Contains(GameSearchText, StringComparison.OrdinalIgnoreCase)
            || game.Provider.Contains(GameSearchText, StringComparison.OrdinalIgnoreCase);
    }
    private async Task AuthenticateAsync(bool register)
    {
        try
        {
            Uri server = ParseServerUri();
            string deviceId = await _deviceIdentityProvider.GetOrCreateDeviceIdAsync(CancellationToken.None);
            AuthSession session = register
                ? await _apiClient.RegisterAsync(server, Username, _password, deviceId, Environment.MachineName, CancellationToken.None)
                : await _apiClient.LoginAsync(server, Username, _password, deviceId, Environment.MachineName, CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.ForDeviceToken(server), session.DeviceToken, CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountName(server), Username.Trim(), CancellationToken.None);
            AuthenticatedUsername = Username.Trim();
            IsAuthenticated = true;
            try
            {
                await ReloadGamesAsync(server, session.DeviceToken);
                await ReloadDevicesAsync(server, session.DeviceToken);
                await ReloadQuotaAsync(server, session.DeviceToken);
                StatusText = $"认证成功，已加载 {Games.Count} 个云端游戏。";
            }
            catch (Exception exception)
            {
                _appLogger.Error("authentication.refresh.failed", exception, "认证成功后的云端数据刷新失败");
                StatusText = $"认证成功，但部分云端数据加载失败：{exception.Message}。可稍后在各页面刷新。";
            }        }
        catch (Exception exception)
        {
            ShowError(register ? "注册失败" : "登录失败", exception);
        }
        finally
        {
            _password = string.Empty;
            PasswordClearRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private async Task ReloadGamesAsync(Uri server, string token)
    {
        IReadOnlyList<CloudGame> games = await _apiClient.ListGamesAsync(server, token, CancellationToken.None);
        Games.Clear();
        foreach (CloudGame game in games) Games.Add(game);
        SelectedGame = Games.FirstOrDefault();
        await RestoreAutomaticSyncProfilesAsync(server, token);
        if (SelectedGame is not null)
        {
            await RestoreLocalProfileAsync(server, token);
            await ReloadSnapshotsAsync(server, token);
            await ReloadRetentionAsync(server, token);
        }
    }

    private async Task RestoreLocalProfileAsync(Uri server, string token)
    {
        if (SelectedGame is null) return;
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalGameProfile? profile = await _localGameProfileStore.GetAsync(serverKey, SelectedGame.GameId, CancellationToken.None);
        if (profile is null) return;

        SaveDirectory = profile.SaveDirectory;
        AutoSnapshotProcessName = profile.ProcessName;
        if (profile.AutoSnapshotEnabled)
        {
            await EnableAutomaticSyncAsync(server, token, SelectedGame.GameId, profile);
        }
        IsAutoSyncEnabled = _autoSyncCoordinator.ActiveGameIds.Contains(SelectedGame.GameId);
    }


    /// <summary>加载所有已启用的本机配置，让每个游戏分别监听对应的进程和存档目录。</summary>
    private async Task RestoreAutomaticSyncProfilesAsync(Uri server, string token)
    {
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        IReadOnlyList<LocalGameProfile> profiles = await _localGameProfileStore.ListAsync(serverKey, CancellationToken.None);
        foreach (LocalGameProfile profile in profiles)
        {
            if (!profile.AutoSnapshotEnabled || !Games.Any(game => game.GameId == profile.GameId)) continue;
            await EnableAutomaticSyncAsync(server, token, profile.GameId, profile);
        }
    }

    private async Task EnableAutomaticSyncAsync(Uri server, string token, string gameId, LocalGameProfile profile)
    {
        if (string.IsNullOrWhiteSpace(profile.ProcessName) || !Directory.Exists(profile.SaveDirectory)) return;
        await _autoSyncCoordinator.EnableAsync(
            gameId,
            new AutoSnapshotProfile(profile.ProcessName, profile.SaveDirectory),
            cancellationToken => RunAutomaticSyncAsync(server, token, gameId, profile.SaveDirectory, cancellationToken),
            CancellationToken.None);
    }

    private async Task RunAutomaticSyncAsync(Uri server, string token, string gameId, string saveDirectory, CancellationToken cancellationToken)
    {
        try
        {
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, gameId, saveDirectory,
                SnapshotTrigger.GameExit, "游戏退出自动同步", false, cancellationToken);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() => ApplySyncResult(result));
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _appLogger.Error("sync.automatic.failed", exception, $"游戏 {gameId} 的自动同步失败");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                StatusText = $"自动同步失败：{exception.Message}");
        }
    }

    private async Task<CloudSyncResult> RunQueuedSyncAsync(
        Uri server, string token, string gameId, string saveDirectory, SnapshotTrigger trigger,
        string description, bool keepLocalOnConflict, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(saveDirectory) || !Directory.Exists(saveDirectory))
            throw new InvalidOperationException("请填写存在的本地存档目录");

        await _syncQueue.WaitAsync(cancellationToken);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _syncCancellation = linked;
        IsSyncing = true;
        SyncProgressValue = 2;
        SyncProgressText = "正在等待同步任务…";
        try
        {
            IProgress<CloudSyncProgress> progress = new Progress<CloudSyncProgress>(ReportSyncProgress);
            return await _cloudSyncService.SyncAsync(server, token, gameId, saveDirectory, trigger, description,
                linked.Token, keepLocalOnConflict, progress);
        }
        finally
        {
            if (ReferenceEquals(_syncCancellation, linked)) _syncCancellation = null;
            IsSyncing = false;
            _syncQueue.Release();
        }
    }

    private void ReportSyncProgress(CloudSyncProgress progress)
    {
        void Apply()
        {
            SyncProgressText = progress.Message;
            SyncProgressValue = progress.Stage switch
            {
                "准备" => 8,
                "扫描" => 22,
                "比对" => 38,
                "上传" when progress.Total > 0 => 38 + 52d * progress.Completed / progress.Total,
                "提交" => 94,
                "完成" => 100,
                _ => SyncProgressValue
            };
        }
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) Apply();
        else _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(Apply);
    }

    private void ApplySyncResult(CloudSyncResult result)
    {
        FileCount = result.FileCount;
        LogicalSizeText = FormatBytes(result.LogicalSize);
        StatusText = result.Status == CloudSyncStatus.RemoteAhead
            ? result.Message + " 请从时间线恢复云端快照，或明确选择保留本机版本。"
            : result.Message;
        SyncSummaryText = result.Status == CloudSyncStatus.Success
            ? $"本次同步：{result.FileCount} 个文件，{FormatBytes(result.LogicalSize)}；上传 {result.UploadedObjectCount} 个内容对象；耗时 {result.Duration.TotalSeconds:0.0} 秒。"
            : $"同步未提交：检测到版本冲突；耗时 {result.Duration.TotalSeconds:0.0} 秒。可恢复云端版本或选择保留本机版本。";
        SyncSummaryText = result.Status == CloudSyncStatus.Success
            ? $"本次同步：{result.FileCount} 个文件，{FormatBytes(result.LogicalSize)}；上传 {result.UploadedObjectCount} 个内容对象；耗时 {result.Duration.TotalSeconds:0.0} 秒。"
            : $"同步未提交：检测到版本冲突；耗时 {result.Duration.TotalSeconds:0.0} 秒。可恢复云端版本或选择保留本机版本。";
        SyncProgressText = result.Status == CloudSyncStatus.Success ? "同步完成" : "需要处理版本冲突";
        if (result.Status == CloudSyncStatus.RemoteAhead) CurrentPage = "时间线";
        if (result.Status == CloudSyncStatus.Success) SyncProgressValue = 100;
    }
    private Task SaveLocalProfileAsync(Uri server, bool autoSnapshotEnabled)
    {
        if (SelectedGame is null) return Task.CompletedTask;
        return _localGameProfileStore.SaveAsync(
            new LocalGameProfile(
                GameSaveServerIdentity.CreateStableKey(server),
                SelectedGame.GameId,
                SaveDirectory,
                AutoSnapshotProcessName,
                autoSnapshotEnabled),
            CancellationToken.None);
    }
    private async Task ReloadRetentionAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await ReloadRetentionAsync(server, token);
            StatusText = "快照保留策略已加载。";
        }
        catch (Exception exception) { ShowError("加载保留策略失败", exception); }
    }

    private async Task ReloadRetentionAsync(Uri server, string token)
    {
        if (SelectedGame is null) return;
        CloudRetentionPolicy policy = await _apiClient.GetRetentionPolicyAsync(
            server, token, SelectedGame.GameId, CancellationToken.None);
        ApplyRetentionPolicy(policy);
    }
    private async Task SaveRetentionAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            if (!int.TryParse(RetentionCountText, out int count) || count is < 1 or > 500)
                throw new InvalidOperationException("保留数量必须是 1 到 500 之间的整数");
            if (!int.TryParse(RetentionDaysText, out int days) || days is < 0 or > 3650)
                throw new InvalidOperationException("保留天数必须是 0 到 3650 之间的整数，0 表示不按时间清理");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudRetentionPolicy policy = await _apiClient.UpdateRetentionPolicyAsync(
                server, token, SelectedGame.GameId, RetentionEnabled, count, days, CancellationToken.None);
            ApplyRetentionPolicy(policy);
            StatusText = policy.Enabled ? "快照自动保留策略已启用。" : "快照自动保留策略已关闭。";
        }
        catch (Exception exception) { ShowError("保存保留策略失败", exception); }
    }

    private async Task CleanupRetentionAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudRetentionCleanupResult result = await _apiClient.CleanupRetentionAsync(
                server, token, SelectedGame.GameId, CancellationToken.None);
            await ReloadSnapshotsAsync(server, token);
            await ReloadQuotaAsync(server, token);
            StatusText = $"保留策略执行完成，删除 {result.DeletedSnapshotCount} 个历史快照。";
        }
        catch (Exception exception) { ShowError("执行保留策略失败", exception); }
    }

    private void ApplyRetentionPolicy(CloudRetentionPolicy policy)
    {
        RetentionEnabled = policy.Enabled;
        RetentionCountText = policy.RetentionCount.ToString();
        RetentionDaysText = policy.RetentionDays.ToString();
    }
    private async Task ReloadQuotaAsync()
    {
        try
        {
            Uri server = ParseServerUri();
            await ReloadQuotaAsync(server, await RequireDeviceTokenAsync(server));
            StatusText = "存储容量已刷新。";
        }
        catch (Exception exception) { ShowError("加载存储容量失败", exception); }
    }

    private async Task ReloadQuotaAsync(Uri server, string token)
    {
        CloudQuota quota = await _apiClient.GetQuotaAsync(server, token, CancellationToken.None);
        QuotaUsageText = $"已用 {FormatBytes(quota.UsedBytes)} / {FormatBytes(quota.QuotaBytes)}，剩余 {FormatBytes(quota.RemainingBytes)}";
    }
    private async Task ReloadDevicesAsync()
    {
        try
        {
            Uri server = ParseServerUri();
            await ReloadDevicesAsync(server, await RequireDeviceTokenAsync(server));
            StatusText = $"已加载 {Devices.Count} 台登记设备。";
        }
        catch (Exception exception) { ShowError("加载设备失败", exception); }
    }

    private async Task ReloadDevicesAsync(Uri server, string token)
    {
        IReadOnlyList<CloudDevice> devices = await _apiClient.ListDevicesAsync(server, token, CancellationToken.None);
        Devices.Clear();
        foreach (CloudDevice device in devices) Devices.Add(device);
        SelectedDevice = Devices.FirstOrDefault(device => device.Active);
    }

    private async Task RevokeDeviceAsync()
    {
        try
        {
            if (SelectedDevice is null) throw new InvalidOperationException("请先选择要撤销的设备");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await _apiClient.RevokeDeviceAsync(server, token, SelectedDevice.DeviceId, CancellationToken.None);
            await ReloadDevicesAsync(server, token);
            StatusText = "设备 Token 已撤销。";
        }
        catch (Exception exception) { ShowError("撤销设备失败", exception); }
    }
    private async Task LoadLocalProfileFromUiAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            await RestoreLocalProfileAsync(server, await RequireDeviceTokenAsync(server));
            StatusText = "本机游戏配置已加载。";
        }
        catch (Exception exception) { ShowError("加载本机配置失败", exception); }
    }

    private async Task SuggestSaveDirectoriesAsync()
    {
        try
        {
            string gameName = SelectedGame?.Name ?? NewGameName;
            if (string.IsNullOrWhiteSpace(gameName)) throw new InvalidOperationException("请先选择或输入游戏名称");
            IReadOnlyList<string> suggestions = await _savePathSuggestionService.SuggestAsync(gameName, CancellationToken.None);
            SaveDirectorySuggestions.Clear();
            foreach (string suggestion in suggestions) SaveDirectorySuggestions.Add(suggestion);
            SelectedSaveDirectorySuggestion = SaveDirectorySuggestions.FirstOrDefault();
            StatusText = suggestions.Count == 0
                ? "未找到常见存档目录；请手动选择正确目录。"
                : $"找到 {suggestions.Count} 个常见存档目录候选，请确认后再同步。";
        }
        catch (Exception exception) { ShowError("查找存档目录失败", exception); }
    }
    private async Task DiscoverGamesAsync()
    {
        try
        {
            StatusText = "正在扫描 Steam、Epic 与 GOG 的本机安装信息…";
            IReadOnlyList<DiscoveredGame> games = await _gameDiscoveryService.DiscoverAsync(CancellationToken.None);
            DiscoveredGames.Clear();
            foreach (DiscoveredGame game in games) DiscoveredGames.Add(game);
            SelectedDiscoveredGame = DiscoveredGames.FirstOrDefault();
            StatusText = $"已发现 {DiscoveredGames.Count} 个安装游戏。选择后会预填游戏名称和进程名；存档目录仍需手动确认。";
        }
        catch (Exception exception) { ShowError("扫描本机游戏失败", exception); }
    }

    private async Task CreateGameAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(NewGameName)) throw new InvalidOperationException("请先输入游戏名称");
            string normalizedName = NewGameName.Trim();
            if (Games.Any(game => string.Equals(game.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("已添加同名游戏；同一账号下游戏名称不能重复。");
            }
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string provider = SelectedDiscoveredGame?.Provider ?? "CUSTOM";
            string? providerGameId = SelectedDiscoveredGame?.ProviderGameId;
            CloudGame game = await _apiClient.CreateGameAsync(server, token, normalizedName, provider, providerGameId, CancellationToken.None);
            await ReloadGamesAsync(server, token);
            SelectedGame = Games.FirstOrDefault(item => item.GameId == game.GameId);
            await ReloadSnapshotsAsync(server, token);
            await ReloadRetentionAsync(server, token);
            await ReloadQuotaAsync(server, token);
            NewGameName = string.Empty;
            StatusText = $"已创建云端游戏：{game.Name}。";
        }
        catch (Exception exception) { ShowError("创建游戏失败", exception); }
    }

    /// <summary>删除云端游戏及全部云端快照，并移除这台电脑的对应同步配置。</summary>
    private async Task DeleteGameAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择要删除的游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string gameId = SelectedGame.GameId;
            string gameName = SelectedGame.Name;
            await _autoSyncCoordinator.DisableAsync(gameId);
            IsAutoSyncEnabled = false;
            await _apiClient.DeleteGameAsync(server, token, gameId, CancellationToken.None);
            await _localGameProfileStore.DeleteAsync(GameSaveServerIdentity.CreateStableKey(server), gameId, CancellationToken.None);
            Games.Remove(SelectedGame);
            Snapshots.Clear();
            SelectedSnapshot = null;
            SelectedGame = Games.FirstOrDefault();
            if (SelectedGame is not null)
            {
                await RestoreLocalProfileAsync(server, token);
                await ReloadSnapshotsAsync(server, token);
            }
            await ReloadQuotaAsync(server, token);
            StatusText = $"已删除游戏“{gameName}”及其全部云端存档。";
        }
        catch (Exception exception) { ShowError("删除游戏失败", exception); }
    }

    private async Task AccountActionAsync()
    {
        if (!IsAuthenticated)
        {
            NavigateTo("账户");
            return;
        }
        await LogoutAsync();
    }

    private async Task LogoutAsync()
    {
        try
        {
            Uri server = ParseServerUri();
            await _autoSyncCoordinator.DisableAllAsync();
            await _credentialStore.DeleteAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
            await _credentialStore.DeleteAsync(CredentialTargets.ForAccountName(server), CancellationToken.None);
            IsAutoSyncEnabled = false;
            IsAuthenticated = false;
            AuthenticatedUsername = string.Empty;
            Games.Clear(); Snapshots.Clear(); Devices.Clear();
            SelectedGame = null; SelectedSnapshot = null; SelectedDevice = null;
            QuotaUsageText = "尚未加载存储容量";
            NavigateTo("账户");
            StatusText = "已退出登录；本机游戏存档文件不会被删除。";
        }
        catch (Exception exception) { ShowError("退出登录失败", exception); }
    }
    private async Task BuildManifestAsync()
    {
        try
        {
            StatusText = "正在完整扫描目录并计算 SHA-256…";
            IReadOnlyList<SnapshotFile> files = await _manifestBuilder.BuildAsync(SaveDirectory, CancellationToken.None);
            FileCount = files.Count;
            LogicalSizeText = FormatBytes(files.Sum(file => file.Size));
            StatusText = "Manifest 已构建，Hash 缓存已写入本地 SQLite。";
        }
        catch (Exception exception) { ShowError("扫描失败", exception); }
    }

    private async Task SyncAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, SelectedGame.GameId, SaveDirectory,
                SnapshotTrigger.Manual, "手动同步", false, CancellationToken.None);
            ApplySyncResult(result);
            await ReloadSnapshotsAsync(server, token);
            await ReloadQuotaAsync(server, token);
        }
        catch (OperationCanceledException) { StatusText = "同步已取消；下次同步会安全复用已上传内容。"; }
        catch (Exception exception) { ShowError("同步失败", exception); }
    }

    private async Task KeepLocalConflictAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, SelectedGame.GameId, SaveDirectory,
                SnapshotTrigger.Manual, "多设备冲突：保留本机版本", true, CancellationToken.None);
            ApplySyncResult(result);
            await ReloadSnapshotsAsync(server, token);
            await ReloadQuotaAsync(server, token);
        }
        catch (OperationCanceledException) { StatusText = "同步已取消。"; }
        catch (Exception exception) { ShowError("保留本机版本失败", exception); }
    }

    private async Task ReloadSnapshotsFromUiAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            await ReloadSnapshotsAsync(server, await RequireDeviceTokenAsync(server));
            StatusText = $"已加载 {Snapshots.Count} 个快照版本。";
        }
        catch (Exception exception) { ShowError("加载时间线失败", exception); }
    }

    private async Task ReloadSnapshotsAsync(Uri server, string token)
    {
        if (SelectedGame is null) return;
        IReadOnlyList<CloudSnapshotSummary> snapshots = await _apiClient.ListSnapshotsAsync(
            server, token, SelectedGame.GameId, 100, CancellationToken.None);
        Snapshots.Clear();
        foreach (CloudSnapshotSummary snapshot in snapshots) Snapshots.Add(snapshot);
        SelectedSnapshot = Snapshots.FirstOrDefault();
    }

    private async Task RestoreAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            if (SelectedSnapshot is null) throw new InvalidOperationException("请从时间线选择要恢复的快照");
            if (string.IsNullOrWhiteSpace(SaveDirectory)) throw new InvalidOperationException("请先填写本地存档目录");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            StatusText = "正在下载、校验并安全恢复快照…";
            RestoreResult result = await _safeRestoreService.RestoreAsync(
                server, token, SelectedGame.GameId, SelectedSnapshot.SnapshotId, SaveDirectory, CancellationToken.None);
            StatusText = result.SafetyBackupDirectory is null
                ? $"已恢复快照 {result.SnapshotId}。"
                : $"已恢复快照 {result.SnapshotId}；原存档安全备份：{result.SafetyBackupDirectory}";
        }
        catch (Exception exception) { ShowError("恢复存档失败", exception); }
    }


    private async Task LoadRestorePreviewAsync()
    {
        try
        {
            if (SelectedGame is null || SelectedSnapshot is null)
                throw new InvalidOperationException("请先选择游戏和要预览的快照");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudSnapshotManifest manifest = await _apiClient.GetSnapshotAsync(server, token, SelectedGame.GameId,
                SelectedSnapshot.SnapshotId, CancellationToken.None);
            long totalSize = manifest.Files.Sum(file => file.Size);
            string examples = string.Join("、", manifest.Files.Take(3).Select(file => file.RelativePath));
            RestorePreviewText = $"将恢复 {manifest.Files.Count} 个文件，共 {FormatBytes(totalSize)}。" +
                (string.IsNullOrWhiteSpace(examples) ? string.Empty : $" 示例：{examples}");
            StatusText = "恢复预览已加载；真正恢复前仍会创建安全备份并逐文件校验。";
        }
        catch (Exception exception) { ShowError("加载恢复预览失败", exception); }
    }

    private async Task ExportSnapshotAsync()
    {
        try
        {
            if (SelectedGame is null || SelectedSnapshot is null)
                throw new InvalidOperationException("请先选择游戏和要导出的快照");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string name = string.Concat(SelectedGame.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            string destination = Path.Combine(downloads, $"{name}-{SelectedSnapshot.CreateTime:yyyyMMdd-HHmmss}.zip");
            StatusText = "正在下载并校验快照内容，然后导出 ZIP…";
            string exported = await _snapshotExportService.ExportAsync(server, token, SelectedGame.GameId,
                SelectedSnapshot.SnapshotId, destination, CancellationToken.None);
            StatusText = $"快照已导出到：{exported}";
        }
        catch (Exception exception) { ShowError("导出快照失败", exception); }
    }
    /// <summary>删除已明确确认的历史快照；服务端会拒绝删除当前同步 HEAD。</summary>
    private async Task DeleteSnapshotAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            if (SelectedSnapshot is null) throw new InvalidOperationException("请从时间线选择要删除的历史快照");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string snapshotId = SelectedSnapshot.SnapshotId;
            await _apiClient.DeleteSnapshotAsync(
                server, token, SelectedGame.GameId, snapshotId, CancellationToken.None);
            await ReloadSnapshotsAsync(server, token);
            await ReloadQuotaAsync(server, token);
            StatusText = $"已删除历史快照 {snapshotId}；未被其他快照引用的内容将按云端清理策略回收。";
        }
        catch (Exception exception) { ShowError("删除历史快照失败", exception); }
    }

    private async Task StartAutoSnapshotAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            if (string.IsNullOrWhiteSpace(SaveDirectory) || !Directory.Exists(SaveDirectory))
                throw new InvalidOperationException("请填写存在的本地存档目录");
            if (string.IsNullOrWhiteSpace(AutoSnapshotProcessName))
                throw new InvalidOperationException("请填写游戏进程名，例如 eldenring.exe");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            LocalGameProfile profile = new(GameSaveServerIdentity.CreateStableKey(server), SelectedGame.GameId,
                SaveDirectory, AutoSnapshotProcessName, true);
            await SaveLocalProfileAsync(server, true);
            await EnableAutomaticSyncAsync(server, token, SelectedGame.GameId, profile);
            IsAutoSyncEnabled = true;
            StatusText = "自动同步已启用：每个已启用的游戏都会独立监听，游戏退出后排队增量同步。";
        }
        catch (Exception exception) { ShowError("启用自动同步失败", exception); }
    }

    private async Task StopAutoSnapshotAsync()
    {
        if (SelectedGame is null) return;
        Uri server = ParseServerUri();
        await _autoSyncCoordinator.DisableAsync(SelectedGame.GameId);
        await SaveLocalProfileAsync(server, false);
        IsAutoSyncEnabled = false;
        StatusText = "已停止当前游戏的自动同步；其他游戏的自动同步不受影响。";
    }

    private async Task<string> RequireDeviceTokenAsync(Uri server)
    {
        string? token = await _credentialStore.ReadAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("当前服务端没有设备 Token，请先注册或登录")
            : token;
    }

    private Uri ParseServerUri() => GameSaveServerIdentity.ParseAndValidate(ServerAddress);

    private void ShowError(string operation, Exception exception)
    {
        _appLogger.Error("operation.failed", exception, operation);
        StatusText = exception is GameSaveApiException apiException
            ? $"{operation} [{apiException.Code}]：{apiException.Message}"
            : $"{operation}：{exception.Message}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}