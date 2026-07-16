using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using GameSaveManager.App.Common;
using GameSaveManager.App.Theming;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Device;
using GameSaveManager.Application.Diagnostics;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Files;
using GameSaveManager.Application.Monitoring;
using GameSaveManager.Application.Launching;
using GameSaveManager.Application.Restores;
using GameSaveManager.Application.Security;
using GameSaveManager.Application.Settings;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Sync;
using GameSaveManager.Application.Startup;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.App.ViewModels;

/// <summary>主窗口交互编排；业务 I/O 均通过 Application 服务执行。</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SaveManifestBuilder _manifestBuilder;
    private readonly ISaveDirectoryPreviewService _saveDirectoryPreviewService;
    private readonly IGameSaveApiClient _apiClient;
    private readonly CloudSyncService _cloudSyncService;
    private readonly SafeRestoreService _safeRestoreService;
    private readonly IAutoSyncCoordinator _autoSyncCoordinator;
    private readonly SnapshotExportService _snapshotExportService;
    private readonly SemaphoreSlim _syncQueue = new(1, 1);
    private CancellationTokenSource? _syncCancellation;
    private readonly IGameDiscoveryService _gameDiscoveryService;
    private readonly ISaveLocationDetector _saveLocationDetector;
    private readonly IExecutableGameIdentityFactory _executableGameIdentityFactory;
    private readonly IRuntimeSaveLearningService _runtimeSaveLearningService;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly ILocalGameProfileStore _localGameProfileStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;
    private readonly IAppLogger _appLogger;
    private readonly IAutoStartService _autoStartService;
    private readonly IServerAddressStore _serverAddressStore;
    private readonly IManifestUpdateService _manifestUpdateService;
    private readonly IRegistrySaveSnapshotService _registrySaveSnapshotService;

    private string _serverAddress = "http://localhost:8080";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _newGameName = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _additionalSaveRootPath = string.Empty;
    private string _registrySaveKeyPath = string.Empty;
    private string _autoSnapshotProcessName = string.Empty;
    private string _autoSnapshotExecutablePath = string.Empty;
    private int _runtimeStatusVersion;
    private readonly Dictionary<string, LocalGameProfile> _localGameProfiles = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _runtimeStatusTimer;
    private string _statusText = "请先注册或登录，然后选择云端游戏并配置本地存档目录。";
    private string _manifestUpdateStatusText = "正在读取离线规则版本…";
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
    private string _saveDirectoryPreviewText = "选择目录后先查看预览，确认后才能同步。";
    private string? _previewedSaveDirectory;
    private string? _previewedSaveDirectoryFingerprint;
    private bool _isLightTheme;
    private bool _autoStartEnabled;
    private string _quotaUsageText = "尚未加载存储容量";
    private bool _retentionEnabled;
    private string _retentionCountText = "50";
    private string _retentionDaysText = "0";
    private CloudGame? _selectedGame;
    private CloudSnapshotSummary? _selectedSnapshot;
    private DiscoveredGame? _selectedDiscoveredGame;
    private SaveLocationCandidate? _selectedSaveLocationCandidate;
    private SaveRootRule? _selectedAdditionalSaveRoot;
    private RegistrySaveRule? _selectedRegistrySaveRule;
    private bool _isSaveDirectoryConfirmed;
    private IReadOnlyList<FileMetadataSnapshot>? _learningBefore;
    private CancellationTokenSource? _learningCancellation;
    private CloudDevice? _selectedDevice;

    public MainViewModel(
        SaveManifestBuilder manifestBuilder,
        ISaveDirectoryPreviewService saveDirectoryPreviewService,
        IGameSaveApiClient apiClient,
        CloudSyncService cloudSyncService,
        SafeRestoreService safeRestoreService,
        IAutoSyncCoordinator autoSyncCoordinator,
        SnapshotExportService snapshotExportService,
        IGameDiscoveryService gameDiscoveryService,
        ISaveLocationDetector saveLocationDetector,
        IExecutableGameIdentityFactory executableGameIdentityFactory,
        IRuntimeSaveLearningService runtimeSaveLearningService,
        IGameLaunchService gameLaunchService,
        ILocalGameProfileStore localGameProfileStore,
        ICredentialStore credentialStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        IAppLogger appLogger,
        IAutoStartService autoStartService,
        IServerAddressStore serverAddressStore,
        IManifestUpdateService manifestUpdateService,
        IRegistrySaveSnapshotService registrySaveSnapshotService)
    {
        _manifestBuilder = manifestBuilder;
        _saveDirectoryPreviewService = saveDirectoryPreviewService;
        _apiClient = apiClient;
        _cloudSyncService = cloudSyncService;
        _safeRestoreService = safeRestoreService;
        _autoSyncCoordinator = autoSyncCoordinator;
        _snapshotExportService = snapshotExportService;
        _gameDiscoveryService = gameDiscoveryService;
        _saveLocationDetector = saveLocationDetector;
        _executableGameIdentityFactory = executableGameIdentityFactory;
        _runtimeSaveLearningService = runtimeSaveLearningService;
        _gameLaunchService = gameLaunchService;
        _localGameProfileStore = localGameProfileStore;
        _credentialStore = credentialStore;
        _deviceIdentityProvider = deviceIdentityProvider;
        _appLogger = appLogger;
        _autoStartService = autoStartService;
        _serverAddressStore = serverAddressStore;
        _manifestUpdateService = manifestUpdateService;
        _registrySaveSnapshotService = registrySaveSnapshotService;
        _autoStartEnabled = autoStartService.IsEnabled();
        _runtimeStatusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _runtimeStatusTimer.Tick += (_, _) => RefreshGameRuntimeStatus();
        _runtimeStatusTimer.Start();

        RegisterCommand = new AsyncCommand(() => AuthenticateAsync(true));
        LoginCommand = new AsyncCommand(() => AuthenticateAsync(false));
        CreateGameCommand = new AsyncCommand(CreateGameAsync, CanCreateGame);
        DeleteGameCommand = new AsyncCommand(DeleteGameAsync, () => IsAuthenticated && SelectedGame is not null);
        LogoutCommand = new AsyncCommand(LogoutAsync, () => IsAuthenticated);
        AccountActionCommand = new AsyncCommand(AccountActionAsync);
        BuildManifestCommand = new AsyncCommand(BuildManifestAsync);
        SyncCommand = new AsyncCommand(SyncAsync, CanSynchronize);
        RetrySyncCommand = new AsyncCommand(SyncAsync, CanSynchronize);
        CancelSyncCommand = new DelegateCommand(_ => _syncCancellation?.Cancel(), _ => IsSyncing);
        ReloadSnapshotsCommand = new AsyncCommand(ReloadSnapshotsFromUiAsync);
        DeleteSnapshotCommand = new AsyncCommand(DeleteSnapshotAsync);
        RestoreCommand = new AsyncCommand(RestoreAsync, CanRestore);
        LoadRestorePreviewCommand = new AsyncCommand(LoadRestorePreviewAsync);
        ExportSnapshotCommand = new AsyncCommand(ExportSnapshotAsync);
        StartAutoSnapshotCommand = new AsyncCommand(StartAutoSnapshotAsync, CanStartAutoSnapshot);
        StopAutoSnapshotCommand = new AsyncCommand(StopAutoSnapshotAsync, () => SelectedGame is not null && IsAutoSyncEnabled);
        DiscoverGamesCommand = new AsyncCommand(DiscoverGamesAsync);
        SuggestSaveDirectoriesCommand = new AsyncCommand(SuggestSaveDirectoriesAsync);
        ConfirmSaveDirectoryCommand = new AsyncCommand(ConfirmSaveDirectoryAsync, CanUseSaveDirectory);
        PreviewSaveDirectoryCommand = new AsyncCommand(PreviewSaveDirectoryAsync, CanUseSaveDirectory);
        StartSaveLearningCommand = new AsyncCommand(StartSaveLearningAsync, CanStartSaveLearning);
        CompleteSaveLearningCommand = new AsyncCommand(CompleteSaveLearningAsync, () => _learningBefore is not null && !(_learningCancellation?.IsCancellationRequested ?? false));
        CancelSaveLearningCommand = new DelegateCommand(_ => CancelSaveLearning(), _ => _learningBefore is not null);
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
        LaunchGameCommand = new AsyncCommand(LaunchGameAsync, CanLaunchGame);
        ToggleThemeCommand = new DelegateCommand(_ => ToggleTheme());
        ToggleAutoStartCommand = new AsyncCommand(ToggleAutoStartAsync);
        UpdateManifestCommand = new AsyncCommand(UpdateManifestAsync);
        AddAdditionalSaveRootCommand = new AsyncCommand(AddAdditionalSaveRootAsync);
        RemoveAdditionalSaveRootCommand = new AsyncCommand(RemoveAdditionalSaveRootAsync);
        AddRegistrySaveRuleCommand = new AsyncCommand(AddRegistrySaveRuleAsync);
        RemoveRegistrySaveRuleCommand = new AsyncCommand(RemoveRegistrySaveRuleAsync);
        FilteredGames = CollectionViewSource.GetDefaultView(Games);
        FilteredGames.Filter = MatchesGameSearch;
        Games.CollectionChanged += (_, _) => { FilteredGames.Refresh(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasGames))); };
    }

    public ObservableCollection<CloudGame> Games { get; } = [];
    public ObservableCollection<CloudSnapshotSummary> Snapshots { get; } = [];
    public ObservableCollection<DiscoveredGame> DiscoveredGames { get; } = [];
    public ObservableCollection<SaveLocationCandidate> SaveLocationCandidates { get; } = [];
    public ObservableCollection<SaveRootRule> AdditionalSaveRoots { get; } = [];
    public ObservableCollection<RegistrySaveRule> RegistrySaveRules { get; } = [];
    public ObservableCollection<CloudDevice> Devices { get; } = [];
    public ICollectionView FilteredGames { get; }
    public bool HasGames => Games.Count > 0;

    public string ServerAddress { get => _serverAddress; set => SetField(ref _serverAddress, value); }
    public string Username { get => _username; set => SetField(ref _username, value); }
    public string NewGameName { get => _newGameName; set => SetField(ref _newGameName, value); }
    public string SaveDirectory { get => _saveDirectory; set { if (SetField(ref _saveDirectory, value)) { IsSaveDirectoryConfirmed = false; _previewedSaveDirectory = null; _previewedSaveDirectoryFingerprint = null; SaveDirectoryPreviewText = "目录已变化，请重新预览。"; } } }
    public string AdditionalSaveRootPath { get => _additionalSaveRootPath; set => SetField(ref _additionalSaveRootPath, value); }
    public string RegistrySaveKeyPath { get => _registrySaveKeyPath; set => SetField(ref _registrySaveKeyPath, value); }
    public bool IsSaveDirectoryConfirmed { get => _isSaveDirectoryConfirmed; private set { if (SetField(ref _isSaveDirectoryConfirmed, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveDirectoryConfirmationText))); } }
    public string SaveDirectoryConfirmationText => IsSaveDirectoryConfirmed ? "已确认，可同步" : "待确认，禁止同步";
    public string AutoSnapshotProcessName { get => _autoSnapshotProcessName; set => SetField(ref _autoSnapshotProcessName, value); }
    public string AutoSnapshotExecutablePath { get => _autoSnapshotExecutablePath; private set => SetField(ref _autoSnapshotExecutablePath, value); }
    public int RuntimeStatusVersion { get => _runtimeStatusVersion; private set => SetField(ref _runtimeStatusVersion, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public string ManifestUpdateStatusText { get => _manifestUpdateStatusText; private set => SetField(ref _manifestUpdateStatusText, value); }
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
    public bool AutoStartEnabled
    {
        get => _autoStartEnabled;
        private set
        {
            if (SetField(ref _autoStartEnabled, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoStartSelectionIndex)));
        }
    }
    public int AutoStartSelectionIndex => AutoStartEnabled ? 1 : 0;
    public int ThemeSelectionIndex => IsLightTheme ? 1 : 0;
    public string AutoStartText => AutoStartEnabled ? "已启用开机启动" : "启用开机启动";
    public bool IsLightTheme
    {
        get => _isLightTheme;
        private set
        {
            if (!SetField(ref _isLightTheme, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeToggleText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThemeSelectionIndex)));
        }
    }
    public string AuthenticatedUsername { get => _authenticatedUsername; private set => SetField(ref _authenticatedUsername, value); }
    public bool IsAutoSyncEnabled { get => _isAutoSyncEnabled; private set => SetField(ref _isAutoSyncEnabled, value); }
    public bool IsSyncing { get => _isSyncing; private set => SetField(ref _isSyncing, value); }
    public string SyncProgressText { get => _syncProgressText; private set => SetField(ref _syncProgressText, value); }
    public double SyncProgressValue { get => _syncProgressValue; private set => SetField(ref _syncProgressValue, value); }
    public string SyncSummaryText { get => _syncSummaryText; private set => SetField(ref _syncSummaryText, value); }
    public string RestorePreviewText { get => _restorePreviewText; private set => SetField(ref _restorePreviewText, value); }
    public string SaveDirectoryPreviewText { get => _saveDirectoryPreviewText; private set => SetField(ref _saveDirectoryPreviewText, value); }
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
                SaveDirectory = string.Empty;
                AutoSnapshotProcessName = string.Empty;
                AutoSnapshotExecutablePath = string.Empty;
                SaveLocationCandidates.Clear();
                AdditionalSaveRoots.Clear();
                SelectedAdditionalSaveRoot = null;
                RegistrySaveRules.Clear();
                SelectedRegistrySaveRule = null;
                IsSaveDirectoryConfirmed = false;
                Snapshots.Clear();
                SelectedSnapshot = null;
                ApplyDiscoveredIdentity(value);
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
    public SaveLocationCandidate? SelectedSaveLocationCandidate
    {
        get => _selectedSaveLocationCandidate;
        set
        {
            if (SetField(ref _selectedSaveLocationCandidate, value) && value is not null) SaveDirectory = value.Path;
        }
    }
    public SaveRootRule? SelectedAdditionalSaveRoot { get => _selectedAdditionalSaveRoot; set => SetField(ref _selectedAdditionalSaveRoot, value); }

    public RegistrySaveRule? SelectedRegistrySaveRule { get => _selectedRegistrySaveRule; set => SetField(ref _selectedRegistrySaveRule, value); }

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
    public ICommand ConfirmSaveDirectoryCommand { get; }
    public ICommand PreviewSaveDirectoryCommand { get; }
    public ICommand StartSaveLearningCommand { get; }
    public ICommand CompleteSaveLearningCommand { get; }
    public ICommand CancelSaveLearningCommand { get; }
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
    public ICommand LaunchGameCommand { get; }
    public ICommand ToggleThemeCommand { get; }
    public ICommand ToggleAutoStartCommand { get; }
    public ICommand AddAdditionalSaveRootCommand { get; }
    public ICommand RemoveAdditionalSaveRootCommand { get; }
    public ICommand AddRegistrySaveRuleCommand { get; }
    public ICommand RemoveRegistrySaveRuleCommand { get; }
    public ICommand UpdateManifestCommand { get; }

    public event EventHandler? PasswordClearRequested;
    public event EventHandler? GameCreated;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>密码仅暂存于内存，并由 PasswordBox 调用此方法传入。</summary>
    public void SetPassword(string password) => _password = password;


    /// <summary>启动时尝试恢复已保存的设备会话；失效 Token 只清理凭据，不影响本机文件。</summary>
    public async Task InitializeAsync()
    {
        try
        {
            string? savedServerAddress = await _serverAddressStore.ReadAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(savedServerAddress)) ServerAddress = savedServerAddress;
            await LoadManifestUpdateStatusAsync();
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
    private async Task LoadManifestUpdateStatusAsync()
    {
        try
        {
            ManifestUpdateStatus status = await _manifestUpdateService.GetStatusAsync(CancellationToken.None);
            ManifestUpdateStatusText = FormatManifestUpdateStatus(status);
        }
        catch (Exception exception)
        {
            _appLogger.Error("manifest.status.read_failed", exception, "读取存档识别规则版本失败");
            ManifestUpdateStatusText = "内置离线规则可用；暂时无法读取更新状态";
        }
    }

    private async Task UpdateManifestAsync()
    {
        try
        {
            StatusText = "正在检查存档识别规则更新…";
            ManifestUpdateStatus status = await _manifestUpdateService.UpdateAsync(CancellationToken.None);
            ManifestUpdateStatusText = FormatManifestUpdateStatus(status);
            StatusText = "存档识别规则已更新；下次查找目录会使用新规则。";
        }
        catch (Exception exception)
        {
            ShowError("更新存档识别规则失败", exception);
        }
    }

    private static string FormatManifestUpdateStatus(ManifestUpdateStatus status) =>
        status.UpdatedAt is null
            ? $"规则版本：{status.Version}（使用内置离线规则）"
            : $"规则版本：{status.Version}，更新于 {status.UpdatedAt.Value.LocalDateTime:g}";
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
            string token = await RequireDeviceTokenAsync(server);
            await RestoreLocalProfileAsync(server, token);
            await ReloadSnapshotsAsync(server, token);
            StatusText = $"已选择 {selected.Name}；可以查看最近快照或点击启动。";
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
            await _serverAddressStore.SaveAsync(server.AbsoluteUri.TrimEnd('/'), CancellationToken.None);
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

        _localGameProfiles[profile.GameId] = profile;
        SaveDirectory = profile.SaveDirectory;
        IsSaveDirectoryConfirmed = profile.UserConfirmed;
        AutoSnapshotProcessName = profile.ProcessName;
        AutoSnapshotExecutablePath = profile.ExecutablePath ?? string.Empty;
        AdditionalSaveRoots.Clear();
        foreach (SaveRootRule root in profile.EffectiveSaveRoots.Where(root => !string.Equals(root.RootId, "root", StringComparison.OrdinalIgnoreCase) && !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase))) AdditionalSaveRoots.Add(root);
        RegistrySaveRules.Clear();
        foreach (RegistrySaveRule rule in profile.EffectiveRegistrySaveRules) RegistrySaveRules.Add(rule);
        RefreshGameRuntimeStatus();
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
        _localGameProfiles.Clear();
        foreach (LocalGameProfile profile in profiles)
        {
            _localGameProfiles[profile.GameId] = profile;
            if (!profile.AutoSnapshotEnabled || !Games.Any(game => game.GameId == profile.GameId)) continue;
            await EnableAutomaticSyncAsync(server, token, profile.GameId, profile);
        }
        RefreshGameRuntimeStatus();
    }

    private static IReadOnlyList<string> GetMonitoredProcessNames(LocalGameProfile profile) =>
        GameProcessNameRules.GetEffectiveNames(profile.EffectiveLaunchProfile, profile.ProcessName);
    private async Task EnableAutomaticSyncAsync(Uri server, string token, string gameId, LocalGameProfile profile)
    {
        IReadOnlyList<SaveRootRule> fileRoots = profile.EffectiveSaveRoots
            .Where(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!profile.UserConfirmed || GetMonitoredProcessNames(profile).Count == 0
            || fileRoots.Count == 0 || fileRoots.Any(root => !root.UserConfirmed || !Directory.Exists(root.Path))) return;
        await _autoSyncCoordinator.EnableAsync(
            gameId,
            new AutoSnapshotProfile(GetMonitoredProcessNames(profile), fileRoots.Select(root => root.Path).ToArray()),
            cancellationToken => RunAutomaticSyncAsync(server, token, gameId, cancellationToken),
            CancellationToken.None);
    }

    private async Task RunAutomaticSyncAsync(Uri server, string token, string gameId, CancellationToken cancellationToken)
    {
        try
        {
            LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
                GameSaveServerIdentity.CreateStableKey(server), gameId, cancellationToken);
            if (profile is null) throw new InvalidOperationException("未找到该游戏的本机同步配置。");
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, gameId, profile,
                SnapshotTrigger.GameExit, "游戏退出自动同步", false, cancellationToken);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (SelectedGame?.GameId == gameId) ApplySyncResult(result);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _appLogger.Error("sync.automatic.failed", exception, $"游戏 {gameId} 的自动同步失败");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (SelectedGame?.GameId == gameId) StatusText = $"自动同步失败：{exception.Message}";
            });
        }
    }

    private async Task<CloudSyncResult> RunQueuedSyncAsync(
        Uri server, string token, string gameId, LocalGameProfile profile, SnapshotTrigger trigger,
        string description, bool keepLocalOnConflict, CancellationToken cancellationToken)
    {
        if (!profile.UserConfirmed) throw new InvalidOperationException("该游戏的存档目录尚未确认。");
        IReadOnlyList<SaveRootRule> roots = profile.EffectiveSaveRoots;
        if (roots.Count == 0 || roots.Any(root => !root.UserConfirmed || !Directory.Exists(root.Path)))
            throw new InvalidOperationException("该游戏存在未确认或已失效的存档目录。");

        await _syncQueue.WaitAsync(cancellationToken);
        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _syncCancellation = linked;
        IsSyncing = true;
        SyncProgressValue = 2;
        SyncProgressText = "正在等待同步任务…";
        try
        {
            await PrepareRegistrySnapshotsAsync(server, gameId, profile.EffectiveRegistrySaveRules);
            IProgress<CloudSyncProgress> progress = new Progress<CloudSyncProgress>(ReportSyncProgress);
            return await _cloudSyncService.SyncAsync(server, token, gameId, roots, trigger, description,
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

    private LocalGameProfile GetRequiredLocalProfile(string gameId) =>
        _localGameProfiles.TryGetValue(gameId, out LocalGameProfile? profile)
            ? profile
            : throw new InvalidOperationException("请先保存并确认该游戏的本机存档配置。");
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
        SyncProgressText = result.Status == CloudSyncStatus.Success ? "同步完成" : "需要处理版本冲突";
        if (result.Status == CloudSyncStatus.RemoteAhead) CurrentPage = "时间线";
        if (result.Status == CloudSyncStatus.Success) SyncProgressValue = 100;
    }
    private string GetRegistryCacheDirectory(Uri server, string gameId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSaveManager", "registry",
        GameSaveServerIdentity.CreateStableKey(server), gameId);

    private async Task PrepareRegistrySnapshotsAsync(Uri server, string gameId, IReadOnlyList<RegistrySaveRule>? rules = null)
    {
        IReadOnlyList<RegistrySaveRule> effectiveRules = rules ?? RegistrySaveRules;
        if (effectiveRules.Count == 0) return;
        await _registrySaveSnapshotService.ExportAsync(GetRegistryCacheDirectory(server, gameId), effectiveRules, CancellationToken.None);
    }

    private IReadOnlyList<SaveRootRule> GetConfiguredSaveRoots(Uri server, string gameId)
    {
        if (string.IsNullOrWhiteSpace(SaveDirectory)) return [];
        SaveLocationSource source = SelectedSaveLocationCandidate?.Source ?? SaveLocationSource.Manual;
        int confidence = SelectedSaveLocationCandidate?.Confidence ?? (IsSaveDirectoryConfirmed ? 100 : 0);
        var roots = new List<SaveRootRule> { SaveRootRule.CreateDefault(SaveDirectory, source, confidence, IsSaveDirectoryConfirmed) };
        roots.AddRange(AdditionalSaveRoots);
        if (RegistrySaveRules.Count > 0) roots.Add(new SaveRootRule("registry", GetRegistryCacheDirectory(server, gameId), ["*.json", "**/*.json"], [], SaveLocationSource.Manual, 100, true));
        return roots;
    }

    private async Task AddAdditionalSaveRootAsync()
    {
        if (string.IsNullOrWhiteSpace(AdditionalSaveRootPath) || !Directory.Exists(AdditionalSaveRootPath))
            throw new InvalidOperationException("请填写存在的附加存档目录。");
        string path = Path.GetFullPath(AdditionalSaveRootPath);
        if (string.Equals(path, Path.GetFullPath(SaveDirectory), StringComparison.OrdinalIgnoreCase)
            || AdditionalSaveRoots.Any(root => string.Equals(root.Path, path, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("该存档目录已经在同步列表中。");
        string rootId;
        int index = 2;
        do rootId = $"root{index++}"; while (AdditionalSaveRoots.Any(root => string.Equals(root.RootId, rootId, StringComparison.OrdinalIgnoreCase)));
        AdditionalSaveRoots.Add(new SaveRootRule(rootId, path, [], [], SaveLocationSource.Manual, 100, true));
        AdditionalSaveRootPath = string.Empty;
        StatusText = $"已添加附加存档目录：{path}。下次同步会一并上传。";
        if (IsAuthenticated && SelectedGame is not null) { Uri server = ParseServerUri(); await RefreshAutomaticSyncConfigurationAsync(server, await RequireDeviceTokenAsync(server), await SaveLocalProfileAsync(server, IsAutoSyncEnabled)); }
    }

    private Task AddRegistrySaveRuleAsync()
    {
        string keyPath = RegistrySaveKeyPath.Trim();
        if (!(keyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) || keyPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("注册表存档仅支持 HKCU\\ 或 HKEY_CURRENT_USER\\ 路径。");
        if (RegistrySaveRules.Any(rule => string.Equals(rule.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("该注册表路径已经在同步列表中。");
        int index = 1; string ruleId; do ruleId = $"registry{index++}"; while (RegistrySaveRules.Any(rule => string.Equals(rule.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)));
        RegistrySaveRules.Add(new RegistrySaveRule(ruleId, keyPath, true));
        RegistrySaveKeyPath = string.Empty;
        StatusText = "已添加注册表存档路径；同步前会导出为受控 JSON。";
        return Task.CompletedTask;
    }

    private Task RemoveRegistrySaveRuleAsync()
    {
        if (SelectedRegistrySaveRule is null) throw new InvalidOperationException("请先选择要移除的注册表路径。");
        RegistrySaveRules.Remove(SelectedRegistrySaveRule);
        SelectedRegistrySaveRule = null;
        StatusText = "已移除注册表存档路径；保存或同步后会更新本机配置。";
        return Task.CompletedTask;
    }
    private async Task RemoveAdditionalSaveRootAsync()
    {
        if (SelectedAdditionalSaveRoot is null) throw new InvalidOperationException("请先选择要移除的附加存档目录。");
        AdditionalSaveRoots.Remove(SelectedAdditionalSaveRoot);
        SelectedAdditionalSaveRoot = null;
        StatusText = "已移除附加存档目录；保存或同步后会更新本机配置。";
        if (IsAuthenticated && SelectedGame is not null) { Uri server = ParseServerUri(); await RefreshAutomaticSyncConfigurationAsync(server, await RequireDeviceTokenAsync(server), await SaveLocalProfileAsync(server, IsAutoSyncEnabled)); }
    }
    private async Task<LocalGameProfile> SaveLocalProfileAsync(Uri server, bool autoSnapshotEnabled)
    {
        if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
        await PrepareRegistrySnapshotsAsync(server, SelectedGame.GameId);
        GameIdentity identity = GetCurrentGameIdentity();
        LocalGameProfile profile = new(
            GameSaveServerIdentity.CreateStableKey(server), SelectedGame.GameId,
            identity.Provider, identity.ProviderGameId, identity.InstallDirectory,
            SaveDirectory, AutoSnapshotProcessName, AutoSnapshotExecutablePath,
            SelectedSaveLocationCandidate?.Source ?? SaveLocationSource.Manual,
            SelectedSaveLocationCandidate?.Confidence ?? (IsSaveDirectoryConfirmed ? 100 : 0),
            IsSaveDirectoryConfirmed, autoSnapshotEnabled && IsSaveDirectoryConfirmed, GetConfiguredSaveRoots(server, SelectedGame.GameId), RegistrySaveRules.ToArray(), identity.ExecutablePath, CreateLaunchProfile(identity));
        await _localGameProfileStore.SaveAsync(profile, CancellationToken.None);
        _localGameProfiles[profile.GameId] = profile;
        RefreshGameRuntimeStatus();
        return profile;
    }
    private static GameLaunchProfile? CreateLaunchProfile(GameIdentity identity)
    {
        IReadOnlyList<string> monitoredProcessNames = string.IsNullOrWhiteSpace(identity.ProcessName)
            ? []
            : [Path.GetFileNameWithoutExtension(identity.ProcessName)];
        if (string.Equals(identity.Provider, GameIdentity.Steam, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(identity.ProviderGameId))
            return new GameLaunchProfile(GameLaunchTargetType.StoreUri, $"steam://run/{Uri.EscapeDataString(identity.ProviderGameId)}", null, null, false, monitoredProcessNames);
        if (string.Equals(identity.Provider, GameIdentity.Epic, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(identity.ProviderGameId))
            return new GameLaunchProfile(GameLaunchTargetType.StoreUri, $"com.epicgames.launcher://apps/{Uri.EscapeDataString(identity.ProviderGameId)}?action=launch&silent=true", null, null, false, monitoredProcessNames);
        if (string.IsNullOrWhiteSpace(identity.ExecutablePath)) return null;
        GameLaunchTargetType targetType = string.Equals(Path.GetExtension(identity.ExecutablePath), ".lnk", StringComparison.OrdinalIgnoreCase)
            ? GameLaunchTargetType.Shortcut
            : GameLaunchTargetType.Executable;
        return new GameLaunchProfile(
            targetType,
            identity.ExecutablePath,
            null,
            targetType == GameLaunchTargetType.Executable ? Path.GetDirectoryName(identity.ExecutablePath) : null,
            false,
            monitoredProcessNames);
    }
    private async Task RefreshAutomaticSyncConfigurationAsync(Uri server, string token, LocalGameProfile profile)
    {
        await _autoSyncCoordinator.DisableAsync(profile.GameId);
        if (profile.AutoSnapshotEnabled) await EnableAutomaticSyncAsync(server, token, profile.GameId, profile);
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
            GameIdentity identity = GetCurrentGameIdentity();
            var progress = new Progress<SaveDetectionProgress>(item => StatusText = item.Message);
            IReadOnlyList<SaveLocationCandidate> candidates = await _saveLocationDetector.DetectAsync(identity, progress, CancellationToken.None);
            SaveLocationCandidates.Clear();
            foreach (SaveLocationCandidate candidate in candidates) SaveLocationCandidates.Add(candidate);
            SelectedSaveLocationCandidate = null;
            StatusText = candidates.Count == 0 ? "未找到存档目录候选，可手动选择后预览确认。" : $"找到 {candidates.Count} 个候选目录；请选择并确认后才能同步。";
        }
        catch (Exception exception) { ShowError("检测存档目录失败", exception); }
    }

    private SaveRootRule BuildPrimarySaveRootRule() => SaveRootRule.CreateDefault(
        SaveDirectory, SelectedSaveLocationCandidate?.Source ?? SaveLocationSource.Manual, 100, false);

    private static string CreatePreviewFingerprint(SaveRootRule rule) => string.Join("|",
        Path.GetFullPath(rule.Path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar),
        string.Join(";", rule.IncludePatterns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)),
        string.Join(";", rule.ExcludePatterns.OrderBy(value => value, StringComparer.OrdinalIgnoreCase)));
    private async Task PreviewSaveDirectoryAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SaveDirectory) || !Directory.Exists(SaveDirectory)) throw new InvalidOperationException("请选择存在的存档目录。");
            var rule = BuildPrimarySaveRootRule();
            SaveDirectoryPreview preview = await _saveDirectoryPreviewService.PreviewAsync(rule, CancellationToken.None);
            FileCount = preview.FileCount;
            LogicalSizeText = FormatBytes(preview.TotalSize);
            _previewedSaveDirectory = Path.GetFullPath(SaveDirectory);
            _previewedSaveDirectoryFingerprint = CreatePreviewFingerprint(rule);
            string warnings = preview.Warnings.Count == 0 ? string.Empty : " 警告：" + string.Join("；", preview.Warnings);
            SaveDirectoryPreviewText = $"{preview.FileCount} 个文件，共 {FormatBytes(preview.TotalSize)}；最近修改：{preview.LatestWriteTimeUtc?.ToLocalTime():g}。" + warnings;
            StatusText = "目录预览完成；确认后才会保存并允许同步。";
        }
        catch (Exception exception) { _previewedSaveDirectory = null; _previewedSaveDirectoryFingerprint = null; ShowError("预览存档目录失败", exception); }
    }
    private async Task ConfirmSaveDirectoryAsync()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SaveDirectory) || !Directory.Exists(SaveDirectory)) throw new InvalidOperationException("请选择存在的存档目录。");
            IsSaveDirectoryConfirmed = true;
            if (SelectedSaveLocationCandidate is { } preview)
            {
                FileCount = preview.FileCount;
                LogicalSizeText = FormatBytes(preview.TotalSize);
                StatusText = $"已确认存档目录：{preview.FileCount} 个文件，{FormatBytes(preview.TotalSize)}。现在可以同步。";
            }
            else StatusText = "已确认手动选择的存档目录。现在可以同步。";
            if (SelectedGame is not null) await SaveLocalProfileAsync(ParseServerUri(), false);
        }
        catch (Exception exception) { ShowError("确认存档目录失败", exception); }
    }

    private async Task StartSaveLearningAsync()
    {
        try
        {
            GameIdentity game = GetCurrentGameIdentity();
            if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath)) throw new InvalidOperationException("请先配置游戏 EXE。");
            _learningCancellation?.Cancel();
            _learningCancellation?.Dispose();
            _learningCancellation = new CancellationTokenSource();
            _learningBefore = await _runtimeSaveLearningService.CaptureBeforeAsync(game, _learningCancellation.Token);
            GameLaunchResult launchResult = await LaunchGameAsync(game, _learningCancellation.Token);
            StatusText = launchResult.Warning is null
                ? "已记录文件元数据并确认游戏正在运行；保存并退出后点击完成学习。"
                : $"已记录文件元数据并发送启动请求，但{launchResult.Warning}";
        }
        catch (OperationCanceledException) { StatusText = "存档学习已取消。"; }
        catch (Exception exception) { ShowError("启动存档学习失败", exception); }
    }

    private void CancelSaveLearning()
    {
        _learningCancellation?.Cancel();
        _learningCancellation?.Dispose();
        _learningCancellation = null;
        _learningBefore = null;
        StatusText = "存档学习已取消。";
    }
    private async Task CompleteSaveLearningAsync()
    {
        try
        {
            if (_learningBefore is null) throw new InvalidOperationException("请先启动存档学习。");
            CancellationToken token = _learningCancellation?.Token ?? CancellationToken.None;
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            IReadOnlyList<SaveLocationCandidate> candidates = await _runtimeSaveLearningService.DetectChangesAsync(GetCurrentGameIdentity(), _learningBefore, new Progress<SaveDetectionProgress>(item => StatusText = item.Message), token);
            SaveLocationCandidates.Clear();
            foreach (SaveLocationCandidate candidate in candidates) SaveLocationCandidates.Add(candidate);
            SelectedSaveLocationCandidate = null;
            _learningBefore = null;
            StatusText = $"学习完成：找到 {candidates.Count} 个候选目录，仍需用户确认。";
        }
        catch (Exception exception) { ShowError("完成存档学习失败", exception); }
    }
    private async Task DiscoverGamesAsync()
    {
        try
        {
            StatusText = "正在扫描 Steam、Epic 与 GOG 的本机安装信息…";
            IReadOnlyList<DiscoveredGame> games = await _gameDiscoveryService.DiscoverAsync(CancellationToken.None);
            DiscoveredGames.Clear();
            foreach (DiscoveredGame game in games) DiscoveredGames.Add(game);
            ApplyDiscoveredIdentity(SelectedGame);
            SelectedDiscoveredGame ??= DiscoveredGames.FirstOrDefault();
            StatusText = $"已发现 {DiscoveredGames.Count} 个安装游戏。已自动关联当前云端游戏的 EXE；存档目录仍需手动确认。";
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
            string discoveredProvider = SelectedDiscoveredGame?.Provider ?? GameIdentity.Custom;
            // LOCAL 仅用于客户端识别本地可执行文件，服务端创建云端游戏时应归入自定义游戏。
            string provider = string.Equals(discoveredProvider, GameIdentity.Local, StringComparison.OrdinalIgnoreCase)
                ? GameIdentity.Custom
                : discoveredProvider;
            string? providerGameId = SelectedDiscoveredGame?.ProviderGameId;
            CloudGame game = await _apiClient.CreateGameAsync(server, token, normalizedName, provider, providerGameId, CancellationToken.None);
            await ReloadGamesAsync(server, token);
            SelectedGame = Games.FirstOrDefault(item => item.GameId == game.GameId);
            await ReloadSnapshotsAsync(server, token);
            await ReloadRetentionAsync(server, token);
            await ReloadQuotaAsync(server, token);
            NewGameName = string.Empty;
            CurrentPage = "游戏详情";
            StatusText = $"已创建云端游戏：{game.Name}。请继续配置启动入口和存档保护。";
            GameCreated?.Invoke(this, EventArgs.Empty);
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
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            await PrepareRegistrySnapshotsAsync(server, SelectedGame.GameId);
            IReadOnlyList<SnapshotFile> files = await _manifestBuilder.BuildAsync(GetConfiguredSaveRoots(server, SelectedGame.GameId), CancellationToken.None);
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
            await SaveLocalProfileAsync(server, IsAutoSyncEnabled);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, SelectedGame.GameId, GetRequiredLocalProfile(SelectedGame.GameId),
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
            await SaveLocalProfileAsync(server, IsAutoSyncEnabled);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, SelectedGame.GameId, GetRequiredLocalProfile(SelectedGame.GameId),
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

    /// <summary>恢复与同步共用 _syncQueue，避免恢复移动存档目录时与正在进行的（含自动）同步竞争同一目录。</summary>
    private async Task RestoreAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            if (SelectedSnapshot is null) throw new InvalidOperationException("请从时间线选择要恢复的快照");
            if (string.IsNullOrWhiteSpace(SaveDirectory)) throw new InvalidOperationException("请先填写本地存档目录");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            StatusText = "正在等待同步任务空闲后开始恢复…";
            await _syncQueue.WaitAsync(CancellationToken.None);
            IReadOnlyList<RestoreResult> results;
            try
            {
                StatusText = "正在下载、校验并安全恢复快照…";
                results = await _safeRestoreService.RestoreAsync(
                    server, token, SelectedGame.GameId, SelectedSnapshot.SnapshotId, GetConfiguredSaveRoots(server, SelectedGame.GameId), RegistrySaveRules.ToArray(), CancellationToken.None);
            }
            finally
            {
                _syncQueue.Release();
            }
            int backups = results.Count(item => item.SafetyBackupDirectory is not null);
            StatusText = backups == 0
                ? $"已恢复快照 {SelectedSnapshot.SnapshotId} 到 {results.Count} 个存档目录。"
                : $"已恢复快照 {SelectedSnapshot.SnapshotId} 到 {results.Count} 个存档目录；已保留 {backups} 份原存档安全备份。";
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

    public async Task SetAutoSnapshotExecutablePathAsync(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return;
        AutoSnapshotExecutablePath = executablePath;
        AutoSnapshotProcessName = Path.GetFileName(executablePath);
        if (SelectedGame is not null) await SaveLocalProfileAsync(ParseServerUri(), IsAutoSyncEnabled);
    }

    public string GetGameProtectionStatusText(CloudGame game)
    {
        if (!_localGameProfiles.TryGetValue(game.GameId, out LocalGameProfile? profile)) return "未配置存档";
        if (!profile.UserConfirmed || profile.EffectiveSaveRoots.Any(root => !root.UserConfirmed)) return "存档待确认";
        if (profile.EffectiveSaveRoots.Where(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)).Any(root => !Directory.Exists(root.Path))) return "存档目录失效";
        return profile.AutoSnapshotEnabled ? "已保护 · 自动同步" : "已保护 · 手动同步";
    }
    public string GetGameRuntimeStatusText(CloudGame game)
    {
        if (!_localGameProfiles.TryGetValue(game.GameId, out LocalGameProfile? profile) || profile.EffectiveLaunchProfile is null)
            return "启动配置待验证";
        return IsGameRunning(profile) ? "运行中" : "未启动";
    }

    public async Task AddLocalGameFromExecutableAsync(string executablePath)
    {
        string fullPath = Path.GetFullPath(executablePath);
        GameIdentity identity = string.Equals(Path.GetExtension(fullPath), ".lnk", StringComparison.OrdinalIgnoreCase)
            ? new GameIdentity(Path.GetFileNameWithoutExtension(fullPath), GameIdentity.Local, null, Path.GetDirectoryName(fullPath) ?? string.Empty, fullPath, null)
            : await _executableGameIdentityFactory.CreateAsync(fullPath, CancellationToken.None);
        var discovered = new DiscoveredGame(identity);
        if (!DiscoveredGames.Any(game => string.Equals(game.ExecutablePath, identity.ExecutablePath, StringComparison.OrdinalIgnoreCase))) DiscoveredGames.Add(discovered);
        SelectedDiscoveredGame = discovered;
        AutoSnapshotExecutablePath = identity.ExecutablePath ?? string.Empty;
        AutoSnapshotProcessName = identity.ProcessName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(NewGameName)) NewGameName = identity.Name;
        StatusText = "已读取本地游戏启动入口；确认名称后创建游戏，再配置存档保护。";
    }

    private void ApplyDiscoveredIdentity(CloudGame? game)
    {
        if (game is null) return;
        DiscoveredGame? discovered = FindDiscoveredGame(game);
        if (discovered is null) return;
        _selectedDiscoveredGame = discovered;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDiscoveredGame)));
        AutoSnapshotExecutablePath = discovered.ExecutablePath ?? string.Empty;
        AutoSnapshotProcessName = discovered.ProcessName ?? string.Empty;
    }

    private DiscoveredGame? FindDiscoveredGame(CloudGame game) => DiscoveredGames.FirstOrDefault(discovered =>
        string.Equals(discovered.Provider, game.Provider, StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrWhiteSpace(game.ProviderGameId) &&
        string.Equals(discovered.ProviderGameId, game.ProviderGameId, StringComparison.OrdinalIgnoreCase))
        ?? DiscoveredGames.FirstOrDefault(discovered => string.Equals(discovered.Name, game.Name, StringComparison.OrdinalIgnoreCase));

    private GameIdentity GetCurrentGameIdentity()
    {
        if (SelectedGame is not null)
        {
            DiscoveredGame? discovered = FindDiscoveredGame(SelectedGame);
            if (discovered is not null) return discovered.Identity;
            if (_localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? profile))
            {
                string? executablePath = string.IsNullOrWhiteSpace(AutoSnapshotExecutablePath)
                    ? profile.ExecutablePath
                    : AutoSnapshotExecutablePath;
                string? processName = string.IsNullOrWhiteSpace(AutoSnapshotProcessName)
                    ? profile.ProcessName
                    : AutoSnapshotProcessName;
                string installDirectory = !string.IsNullOrWhiteSpace(executablePath) && File.Exists(executablePath)
                    ? Path.GetDirectoryName(executablePath) ?? profile.InstallDirectory ?? string.Empty
                    : profile.InstallDirectory ?? string.Empty;
                return new GameIdentity(SelectedGame.Name, profile.Provider, profile.ProviderGameId, installDirectory, executablePath, processName);
            }
            string? configuredExecutablePath = string.IsNullOrWhiteSpace(AutoSnapshotExecutablePath) ? null : AutoSnapshotExecutablePath;
            string configuredInstallDirectory = configuredExecutablePath is { Length: > 0 } && File.Exists(configuredExecutablePath)
                ? Path.GetDirectoryName(configuredExecutablePath) ?? string.Empty
                : string.Empty;
            return new GameIdentity(SelectedGame.Name, SelectedGame.Provider, SelectedGame.ProviderGameId,
                configuredInstallDirectory, configuredExecutablePath,
                string.IsNullOrWhiteSpace(AutoSnapshotProcessName) ? null : AutoSnapshotProcessName);
        }
        return SelectedDiscoveredGame?.Identity ?? new GameIdentity(NewGameName, GameIdentity.Custom, null, string.Empty, null, null);
    }

    private Task<GameLaunchResult> LaunchGameAsync(GameIdentity game, CancellationToken cancellationToken = default)
    {
        GameLaunchProfile launchProfile = CreateLaunchProfile(game)
            ?? throw new InvalidOperationException("未找到有效的游戏启动配置。");
        return _gameLaunchService.LaunchAsync(launchProfile, game.InstallDirectory, cancellationToken);
    }

    private async Task LaunchGameAsync(object? parameter)
    {
        if (parameter is not CloudGame game) return;
        if (!_localGameProfiles.TryGetValue(game.GameId, out LocalGameProfile? profile))
        {
            SelectedGame = game;
            CurrentPage = "游戏详情";
            StatusText = "该游戏的启动配置待验证，请在游戏详情中选择正确的 EXE。";
            return;
        }

        GameLaunchProfile? launchProfile = profile.EffectiveLaunchProfile;
        bool invalidLocalTarget = launchProfile is { TargetType: not GameLaunchTargetType.StoreUri } && !File.Exists(launchProfile.Target);
        if (launchProfile is null || invalidLocalTarget)
        {
            SelectedGame = game;
            AutoSnapshotExecutablePath = profile.ExecutablePath ?? string.Empty;
            AutoSnapshotProcessName = profile.ProcessName;
            CurrentPage = "游戏详情";
            StatusText = "游戏启动入口缺失或已经失效，请在游戏详情中重新选择 EXE。";
            return;
        }

        try
        {
            SelectedGame = game;
            SaveDirectory = profile.SaveDirectory;
            IsSaveDirectoryConfirmed = profile.UserConfirmed;
            AutoSnapshotExecutablePath = profile.ExecutablePath ?? launchProfile.Target;

            IReadOnlyList<string> effectiveNames = GameProcessNameRules.GetEffectiveNames(launchProfile, profile.ProcessName);
            launchProfile = launchProfile with { MonitoredProcessNames = effectiveNames };
            string primaryProcessName = launchProfile.TargetType == GameLaunchTargetType.Executable
                ? Path.GetFileName(launchProfile.Target)
                : effectiveNames.FirstOrDefault() is { Length: > 0 } first ? first + ".exe" : string.Empty;
            AutoSnapshotProcessName = primaryProcessName;
            string? repairedExecutablePath = launchProfile.TargetType == GameLaunchTargetType.Executable
                ? launchProfile.Target
                : profile.ExecutablePath;
            string? repairedInstallDirectory = repairedExecutablePath is { Length: > 0 }
                ? Path.GetDirectoryName(repairedExecutablePath)
                : profile.InstallDirectory;

            if (!profile.EffectiveLaunchProfile!.MonitoredProcessNames.SequenceEqual(effectiveNames, StringComparer.OrdinalIgnoreCase) ||
                !string.Equals(profile.ProcessName, primaryProcessName, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(profile.ExecutablePath, repairedExecutablePath, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(profile.InstallDirectory, repairedInstallDirectory, StringComparison.OrdinalIgnoreCase))
            {
                profile = profile with
                {
                    ProcessName = primaryProcessName,
                    ExecutablePath = repairedExecutablePath,
                    InstallDirectory = repairedInstallDirectory,
                    IdentityExecutablePath = repairedExecutablePath,
                    LaunchProfile = launchProfile
                };
                await _localGameProfileStore.SaveAsync(profile, CancellationToken.None);
                _localGameProfiles[profile.GameId] = profile;
            }

            StatusText = $"已发送 {game.Name} 的启动请求，正在确认游戏进程…";
            GameLaunchResult launchResult = await _gameLaunchService.LaunchAsync(
                launchProfile,
                profile.InstallDirectory,
                CancellationToken.None);
            string[] detectedNames = launchResult.DetectedProcesses
                .Where(candidate => candidate.IsStillRunning && !GameProcessNameRules.IsUnsafeGenericName(candidate.ProcessName))
                .Select(candidate => GameProcessNameRules.Normalize(candidate.ProcessName))
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            string[] monitoredNames = effectiveNames.Concat(detectedNames)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (!launchProfile.MonitoredProcessNames.SequenceEqual(monitoredNames, StringComparer.OrdinalIgnoreCase))
            {
                launchProfile = launchProfile with { MonitoredProcessNames = monitoredNames };
                profile = profile with { LaunchProfile = launchProfile };
                await _localGameProfileStore.SaveAsync(profile, CancellationToken.None);
                _localGameProfiles[profile.GameId] = profile;
            }

            StatusText = launchResult.Warning is null
                ? $"已确认 {game.Name} 的游戏进程正在运行。"
                : $"已发送 {game.Name} 的启动请求，但{launchResult.Warning}";
            RefreshGameRuntimeStatus();
        }
        catch (Exception exception)
        {
            ShowError("启动游戏失败", exception);
        }
    }

    private bool IsGameRunning(LocalGameProfile profile) =>
        GetMonitoredProcessNames(profile).Any(name => _runningProcessNames.Contains(GameProcessNameRules.Normalize(name)));

    // 运行状态刷新时先枚举一次全部进程并缓存进程名集合；读写均在 UI 线程，引用整体替换避免读到半更新集合。
    private HashSet<string> _runningProcessNames = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshGameRuntimeStatus()
    {
        _runningProcessNames = SnapshotRunningProcessNames();
        RuntimeStatusVersion++;
    }

    private static HashSet<string> SnapshotRunningProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Process process in Process.GetProcesses())
        {
            try { names.Add(process.ProcessName); }
            catch { /* 个别进程可能拒绝访问其名称，跳过即可 */ }
            finally { process.Dispose(); }
        }
        return names;
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
            if (!IsSaveDirectoryConfirmed) throw new InvalidOperationException("请先确认存档目录后再启用自动同步");
            LocalGameProfile profile = await SaveLocalProfileAsync(server, true);
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
        ClientOperationError error = ClientOperationError.FromException(exception);
        string requestId = string.IsNullOrWhiteSpace(error.RequestId) ? string.Empty : $"（请求 ID：{error.RequestId}）";
        string retry = error.CanRetry
            ? error.SuggestedRetryDelay is { } delay && delay > TimeSpan.Zero
                ? $" 可在约 {Math.Ceiling(delay.TotalSeconds):0} 秒后重试。"
                : " 可稍后重试。"
            : string.Empty;
        StatusText = $"{operation}：{error.UserMessage}{requestId}{retry}";
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private bool CanCreateGame() =>
        IsAuthenticated && !string.IsNullOrWhiteSpace(NewGameName);

    private bool CanLaunchGame(object? parameter) => parameter is CloudGame;

    private static bool IsLaunchTargetValid(GameLaunchProfile? profile)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.Target)) return false;
        if (profile.TargetType == GameLaunchTargetType.StoreUri) return Uri.TryCreate(profile.Target, UriKind.Absolute, out _);
        if (!File.Exists(profile.Target)) return false;
        return string.IsNullOrWhiteSpace(profile.WorkingDirectory) || Directory.Exists(profile.WorkingDirectory);
    }

    private bool CanUseSaveDirectory() =>
        !string.IsNullOrWhiteSpace(SaveDirectory) && Directory.Exists(SaveDirectory);

    private bool CanStartAutoSnapshot()
    {
        if (!IsAuthenticated || SelectedGame is null || IsAutoSyncEnabled || !HasConfirmedExistingFileRoots()) return false;
        if (string.IsNullOrWhiteSpace(AutoSnapshotProcessName)) return false;
        return IsLaunchTargetValid(CreateLaunchProfile(GetCurrentGameIdentity()));
    }
    private bool CanSynchronize() =>
        IsAuthenticated && !IsSyncing && SelectedGame is not null && HasConfirmedExistingFileRoots();

    private bool CanRestore() =>
        CanSynchronize() && SelectedSnapshot is not null &&
        (SelectedGame is null || !_localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? profile) || !IsGameRunning(profile));

    private bool CanStartSaveLearning()
    {
        GameIdentity game = GetCurrentGameIdentity();
        return IsLaunchTargetValid(CreateLaunchProfile(game));
    }

    private bool HasConfirmedExistingFileRoots()
    {
        if (!IsSaveDirectoryConfirmed || string.IsNullOrWhiteSpace(SaveDirectory) || !Directory.Exists(SaveDirectory)) return false;
        return AdditionalSaveRoots.All(root => root.UserConfirmed && Directory.Exists(root.Path));
    }

    private void RaiseCommandStates()
    {
        foreach (ICommand command in new ICommand[]
        {
            CreateGameCommand, DeleteGameCommand, LogoutCommand, SyncCommand, RetrySyncCommand, CancelSyncCommand,
            RestoreCommand, LaunchGameCommand, StartAutoSnapshotCommand, StopAutoSnapshotCommand,
            ConfirmSaveDirectoryCommand, PreviewSaveDirectoryCommand, StartSaveLearningCommand,
            CompleteSaveLearningCommand, CancelSaveLearningCommand
        })
        {
            if (command is AsyncCommand asyncCommand) asyncCommand.RaiseCanExecuteChanged();
            else if (command is DelegateCommand delegateCommand) delegateCommand.RaiseCanExecuteChanged();
        }
    }
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        RaiseCommandStates();
        return true;
    }
}
