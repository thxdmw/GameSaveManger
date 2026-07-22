using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using System.Windows.Threading;
using GameSaveManager.App.Common;
using GameSaveManager.App.Theming;
using GameSaveManager.Application;
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
using GameSaveManager.Application.Updates;
using GameSaveManager.Domain.Snapshots;
using GameSaveManager.Infrastructure.Maintenance;

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
    private readonly Dictionary<string, CancellationTokenSource> _syncCancellations = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SyncConflictContext> _activeConflicts = new(StringComparer.Ordinal);
    private readonly IGameDiscoveryService _gameDiscoveryService;
    private readonly ISaveLocationDetector _saveLocationDetector;
    private readonly IExecutableGameIdentityFactory _executableGameIdentityFactory;
    private readonly IRuntimeSaveLearningService _runtimeSaveLearningService;
    private readonly IGameLaunchService _gameLaunchService;
    private readonly IGameLaunchProfileMerger _gameLaunchProfileMerger;
    private readonly IShortcutResolver _shortcutResolver;
    private readonly ILocalGameProfileStore _localGameProfileStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;
    private readonly ISavePathTemplateService _savePathTemplateService;
    private readonly IAppLogger _appLogger;
    private readonly IAutoStartService _autoStartService;
    private readonly IServerAddressStore _serverAddressStore;
    private readonly IManifestUpdateService _manifestUpdateService;
    private readonly IRegistrySaveSnapshotService _registrySaveSnapshotService;
    private readonly IClientUpdateService _clientUpdateService;
    private readonly IUpdatePreferenceStore _updatePreferenceStore;
    private readonly LocalStorageMaintenanceService _localStorageMaintenanceService;
    private readonly Dictionary<string, string> _shortcutResolutionFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ShortcutResolution> _shortcutResolutions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _launchesInProgress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameSyncUiState> _gameSyncUiStates = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _authenticationGate = new(1, 1);
    private CancellationTokenSource _sessionLifetime = new();
    private long _sessionGeneration;
    private int _authenticationInProgress;

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
    private string _authenticatedUserId = string.Empty;
    private bool _isAutoSyncEnabled;
    private bool _resumeAutomaticSyncAfterConfiguration;
    private bool _isAddGameWizardActive;
    private AddGameWizardReturnState? _addGameWizardReturnState;
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
    private ConfigurationOperationStamp? _learningOperationStamp;
    private CloudDevice? _selectedDevice;
    private ClientUpdatePreferences _updatePreferences = ClientUpdatePreferences.Default;
    private ClientUpdateRelease? _availableUpdate;
    private PreparedClientUpdate? _preparedUpdate;
    private CancellationTokenSource? _updateDownloadCancellation;
    private string _updateStatusText = "尚未检查客户端更新。";
    private bool _isUpdateBusy;
    private bool _isUpdateDownloading;
    private double _updateDownloadProgress;
    private string _localStorageUsageText = "正在统计可清理的本地缓存…";
    private bool _isLocalStorageBusy;

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
        IGameLaunchProfileMerger gameLaunchProfileMerger,
        IShortcutResolver shortcutResolver,
        ILocalGameProfileStore localGameProfileStore,
        ICredentialStore credentialStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        ISavePathTemplateService savePathTemplateService,
        IAppLogger appLogger,
        IAutoStartService autoStartService,
        IServerAddressStore serverAddressStore,
        IManifestUpdateService manifestUpdateService,
        IRegistrySaveSnapshotService registrySaveSnapshotService,
        IClientUpdateService clientUpdateService,
        IUpdatePreferenceStore updatePreferenceStore,
        LocalStorageMaintenanceService? localStorageMaintenanceService = null)
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
        _gameLaunchProfileMerger = gameLaunchProfileMerger;
        _shortcutResolver = shortcutResolver;
        _localGameProfileStore = localGameProfileStore;
        _credentialStore = credentialStore;
        _deviceIdentityProvider = deviceIdentityProvider;
        _savePathTemplateService = savePathTemplateService;
        _appLogger = appLogger;
        _autoStartService = autoStartService;
        _serverAddressStore = serverAddressStore;
        _manifestUpdateService = manifestUpdateService;
        _registrySaveSnapshotService = registrySaveSnapshotService;
        _clientUpdateService = clientUpdateService;
        _updatePreferenceStore = updatePreferenceStore;
        _localStorageMaintenanceService = localStorageMaintenanceService ?? new LocalStorageMaintenanceService();
        _autoStartEnabled = autoStartService.IsEnabled();
        _runtimeStatusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _runtimeStatusTimer.Tick += (_, _) => RefreshGameRuntimeStatus();
        _runtimeStatusTimer.Start();

        RegisterCommand = new AsyncCommand(() => AuthenticateAsync(true), CanStartAuthentication);
        LoginCommand = new AsyncCommand(() => AuthenticateAsync(false), CanStartAuthentication);
        CreateGameCommand = new AsyncCommand(CreateGameAsync, CanCreateGame);
        DeleteGameCommand = new AsyncCommand(
            DeleteGameAsync,
            parameter => IsAuthenticated
                         && parameter is CloudGame game
                         && Games.Any(item => string.Equals(item.GameId, game.GameId, StringComparison.Ordinal)));
        LogoutCommand = new AsyncCommand(LogoutAsync, () => IsAuthenticated);
        AccountActionCommand = new AsyncCommand(AccountActionAsync);
        BuildManifestCommand = new AsyncCommand(BuildManifestAsync);
        SyncCommand = new AsyncCommand(SyncAsync, CanSynchronize);
        RetrySyncCommand = new AsyncCommand(SyncAsync, CanSynchronize);
        CancelSyncCommand = new DelegateCommand(_ => CancelSelectedGameSync(), _ => IsSelectedGameSyncing);
        ReloadSnapshotsCommand = new AsyncCommand(ReloadSnapshotsFromUiAsync);
        DeleteSnapshotCommand = new AsyncCommand(DeleteSnapshotAsync);
        RestoreCommand = new AsyncCommand(RestoreAsync, CanRestore);
        LoadRestorePreviewCommand = new AsyncCommand(LoadRestorePreviewAsync);
        ExportSnapshotCommand = new AsyncCommand(ExportSnapshotAsync);
        StartAutoSnapshotCommand = new AsyncCommand(StartAutoSnapshotAsync, CanStartAutoSnapshot);
        StopAutoSnapshotCommand = new AsyncCommand(StopAutoSnapshotAsync, () => SelectedGame is not null && IsAutoSyncEnabled);
        DiscoverGamesCommand = new AsyncCommand(DiscoverGamesAsync);
        ReloadGamesCommand = new AsyncCommand(ReloadGamesFromUiAsync, () => IsAuthenticated);
        SuggestSaveDirectoriesCommand = new AsyncCommand(SuggestSaveDirectoriesAsync);
        ConfirmSaveDirectoryCommand = new AsyncCommand(ConfirmSaveDirectoryAsync, IsCurrentSavePreviewValid);
        PreviewSaveDirectoryCommand = new AsyncCommand(PreviewSaveDirectoryAsync, CanUseSaveDirectory);
        StartSaveLearningCommand = new AsyncCommand(StartSaveLearningAsync, CanStartSaveLearning);
        CompleteSaveLearningCommand = new AsyncCommand(CompleteSaveLearningAsync, () => _learningBefore is not null && !(_learningCancellation?.IsCancellationRequested ?? false));
        CancelSaveLearningCommand = new DelegateCommand(_ => CancelSaveLearning(), _ => _learningCancellation is not null || _learningBefore is not null);
        LoadLocalProfileCommand = new AsyncCommand(LoadLocalProfileFromUiAsync);
        ReloadDevicesCommand = new AsyncCommand(ReloadDevicesAsync);
        ReloadQuotaCommand = new AsyncCommand(ReloadQuotaAsync);
        ReloadRetentionCommand = new AsyncCommand(ReloadRetentionAsync);
        SaveRetentionCommand = new AsyncCommand(SaveRetentionAsync);
        CleanupRetentionCommand = new AsyncCommand(CleanupRetentionAsync);
        RevokeDeviceCommand = new AsyncCommand(
            RevokeDeviceAsync,
            parameter => parameter is CloudDevice device
                         && Devices.Any(item => string.Equals(item.DeviceId, device.DeviceId, StringComparison.Ordinal)));
        KeepLocalConflictCommand = new AsyncCommand(KeepLocalConflictAsync, HasActiveConflictForSelectedGame);
        NavigateCommand = new DelegateCommand(NavigateTo);
        SelectGameCommand = new AsyncCommand(
            SelectGameAsync,
            parameter => IsAuthenticated
                         && parameter is CloudGame game
                         && Games.Any(item => string.Equals(item.GameId, game.GameId, StringComparison.Ordinal)));
        LaunchGameCommand = new AsyncCommand(LaunchGameAsync, CanLaunchGame);
        ToggleThemeCommand = new DelegateCommand(_ => ToggleTheme());
        ToggleAutoStartCommand = new AsyncCommand(ToggleAutoStartAsync);
        UpdateManifestCommand = new AsyncCommand(UpdateManifestAsync);
        CheckForUpdateCommand = new AsyncCommand(() => CheckForUpdatesAsync(true), () => !IsUpdateBusy);
        DownloadUpdateCommand = new AsyncCommand(DownloadUpdateAsync, () => CanDownloadUpdate);
        CancelUpdateDownloadCommand = new DelegateCommand(_ => _updateDownloadCancellation?.Cancel(), _ => IsUpdateDownloading);
        InstallUpdateCommand = new DelegateCommand(_ => UpdateInstallationRequested?.Invoke(this, EventArgs.Empty), _ => CanInstallUpdate);
        ToggleStartupUpdateCheckCommand = new AsyncCommand(ToggleStartupUpdateCheckAsync, () => !IsUpdateBusy);
        CleanupLocalStorageCommand = new AsyncCommand(CleanupLocalStorageAsync, () => !IsLocalStorageBusy);
        AddAdditionalSaveRootCommand = new AsyncCommand(AddAdditionalSaveRootAsync);
        RemoveAdditionalSaveRootCommand = new AsyncCommand(RemoveAdditionalSaveRootAsync);
        AddRegistrySaveRuleCommand = new AsyncCommand(AddRegistrySaveRuleAsync);
        RemoveRegistrySaveRuleCommand = new AsyncCommand(RemoveRegistrySaveRuleAsync);
        Session = new ApplicationSession(
            () => ServerAddress,
            () => IsAuthenticated,
            () => AuthenticatedUsername,
            () => SelectedGame,
            game => SelectedGame = game,
            _credentialStore);
        SaveConfiguration = new SaveConfigurationViewModel(this);
        AddGameWizard = new AddGameWizardViewModel(this);
        FilteredGames = CollectionViewSource.GetDefaultView(Games);
        FilteredGames.Filter = MatchesGameSearch;
        Games.CollectionChanged += (_, _) => { FilteredGames.Refresh(); PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasGames))); };
        Snapshots.CollectionChanged += (_, _) => PropertyChanged?.Invoke(
            this, new PropertyChangedEventArgs(nameof(RecentSnapshots)));
    }

    public ApplicationSession Session { get; }
    public SaveConfigurationViewModel SaveConfiguration { get; }
    public AddGameWizardViewModel AddGameWizard { get; }
    public ObservableCollection<CloudGame> Games { get; } = [];
    public ObservableCollection<CloudSnapshotSummary> Snapshots { get; } = [];
    public IEnumerable<CloudSnapshotSummary> RecentSnapshots => Snapshots.Take(5);
    public ObservableCollection<DiscoveredGame> DiscoveredGames { get; } = [];
    public ObservableCollection<SaveLocationCandidate> SaveLocationCandidates { get; } = [];
    public ObservableCollection<SaveRootRule> AdditionalSaveRoots { get; } = [];
    public ObservableCollection<RegistrySaveRule> RegistrySaveRules { get; } = [];
    public ObservableCollection<SaveRootPreview> SaveRootPreviews { get; } = [];
    public ObservableCollection<RegistrySavePreview> RegistrySavePreviews { get; } = [];
    public ObservableCollection<CloudDevice> Devices { get; } = [];
    public ICollectionView FilteredGames { get; }
    public bool HasGames => Games.Count > 0;
    public string ClientVersionText { get; } = GetClientVersionText();
    public string ClientReleaseChannelText { get; } = GetClientReleaseChannelText();
    public string UpdateStatusText { get => _updateStatusText; private set => SetField(ref _updateStatusText, value); }
    public bool IsUpdateBusy { get => _isUpdateBusy; private set { if (SetField(ref _isUpdateBusy, value)) NotifyUpdateStateChanged(); } }
    public bool IsUpdateDownloading { get => _isUpdateDownloading; private set { if (SetField(ref _isUpdateDownloading, value)) NotifyUpdateStateChanged(); } }
    public double UpdateDownloadProgress { get => _updateDownloadProgress; private set => SetField(ref _updateDownloadProgress, value); }
    public bool UpdateCheckOnStartup => _updatePreferences.CheckOnStartup;
    public int UpdateCheckOnStartupSelectionIndex => UpdateCheckOnStartup ? 1 : 0;
    public bool CanDownloadUpdate => _availableUpdate is not null && _preparedUpdate is null && !IsUpdateBusy;
    public bool CanInstallUpdate => _preparedUpdate is not null && !IsUpdateBusy;
    public bool HasAvailableUpdate => _availableUpdate is not null;
    public bool HasPreparedUpdate => _preparedUpdate is not null;
    public string LocalStorageUsageText { get => _localStorageUsageText; private set => SetField(ref _localStorageUsageText, value); }
    public bool IsLocalStorageBusy { get => _isLocalStorageBusy; private set => SetField(ref _isLocalStorageBusy, value); }
    public bool IsSaveConfigurationPreviewValid => IsCurrentSavePreviewValid();
    public bool PendingLaunchTargetIsValid => GetPendingLaunchProfileValidationError() is null;

    public string ServerAddress { get => _serverAddress; set => SetField(ref _serverAddress, value); }
    public string Username { get => _username; set => SetField(ref _username, value); }
    public string NewGameName { get => _newGameName; set => SetField(ref _newGameName, value); }
    public string SaveDirectory
    {
        get => _saveDirectory;
        set { if (SetField(ref _saveDirectory, value)) InvalidateSavePreview("目录已变化，请重新预览完整配置。"); }
    }
    public string AdditionalSaveRootPath { get => _additionalSaveRootPath; set => SetField(ref _additionalSaveRootPath, value); }
    public string RegistrySaveKeyPath { get => _registrySaveKeyPath; set => SetField(ref _registrySaveKeyPath, value); }
    public bool IsSaveDirectoryConfirmed { get => _isSaveDirectoryConfirmed; private set { if (SetField(ref _isSaveDirectoryConfirmed, value)) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SaveDirectoryConfirmationText))); } }
    public string SaveDirectoryConfirmationText => IsSaveDirectoryConfirmed ? "已确认，可同步" : "待确认，禁止同步";
    public string AutoSnapshotProcessName { get => _autoSnapshotProcessName; set => SetField(ref _autoSnapshotProcessName, value); }
    public string AutoSnapshotExecutablePath
    {
        get => _autoSnapshotExecutablePath;
        private set
        {
            if (SetField(ref _autoSnapshotExecutablePath, value) && _isAddGameWizardActive)
                AddGameWizard.InvalidateLaunchValidation();
        }
    }
    public int RuntimeStatusVersion { get => _runtimeStatusVersion; private set => SetField(ref _runtimeStatusVersion, value); }
    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (!SetField(ref _statusText, value)) return;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(GlobalStatusText)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameLastErrorText)));
        }
    }
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
    public string GlobalStatusText => StatusText;
    public string SelectedGameHealthText => SelectedGame is null
        ? "尚未选择游戏"
        : GetGameProtectionStatusText(SelectedGame);
    public string SelectedGameLastSyncText =>
        GetSelectedGameSyncState()?.Summary is { Length: > 0 } summary ? summary : "暂无同步记录";
    public string SelectedGameLastErrorText => GetSelectedGameSyncState()?.Error ?? string.Empty;
    public bool IsSelectedGameSyncing => GetSelectedGameSyncState()?.IsSyncing ?? false;
    public bool HasActiveConflict => HasActiveConflictForSelectedGame();
    public string? ActiveConflictRemoteHeadSnapshotId => SelectedGame is not null
        && _activeConflicts.TryGetValue(SelectedGame.GameId, out SyncConflictContext? conflict)
            ? conflict.RemoteHeadSnapshotId
            : null;
    public string SelectedGameSyncProgressText =>
        GetSelectedGameSyncState()?.ProgressText is { Length: > 0 } text ? text : "等待立即备份";
    public double SelectedGameSyncProgressValue => GetSelectedGameSyncState()?.ProgressValue ?? 0;
    public string AuthenticatedUsername { get => _authenticatedUsername; private set => SetField(ref _authenticatedUsername, value); }
    public bool IsAutoSyncEnabled
    {
        get => _isAutoSyncEnabled;
        private set
        {
            if (SetField(ref _isAutoSyncEnabled, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoSyncConfigurationText)));
        }
    }
    public bool IsSyncing { get => _isSyncing; private set => SetField(ref _isSyncing, value); }
    public string SyncProgressText { get => _syncProgressText; private set => SetField(ref _syncProgressText, value); }
    public double SyncProgressValue { get => _syncProgressValue; private set => SetField(ref _syncProgressValue, value); }
    public string SyncSummaryText
    {
        get => _syncSummaryText;
        private set
        {
            if (SetField(ref _syncSummaryText, value))
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameLastSyncText)));
        }
    }
    public string RestorePreviewText { get => _restorePreviewText; private set => SetField(ref _restorePreviewText, value); }
    public string SaveDirectoryPreviewText { get => _saveDirectoryPreviewText; private set => SetField(ref _saveDirectoryPreviewText, value); }
    public string ConnectionStatusText => IsAuthenticated ? "已登录" : "未登录";
    public string AccountActionText => IsAuthenticated ? "退出登录" : "登录";
    public string AccountSummaryText => IsAuthenticated ? $"当前账号：{AuthenticatedUsername}" : "尚未登录";
    public string CloudReadinessText => IsAuthenticated ? "云端同步已就绪" : "登录后启用云端同步";
    public string ThemeToggleText => IsLightTheme ? "切换至深色主题" : "切换至浅色主题";
    public string LaunchDisabledReason => SelectedGame is null ? "请先选择游戏。" : GetLaunchDisabledReason(SelectedGame) ?? string.Empty;
    public string AutoSyncConfigurationText
    {
        get
        {
            if (SelectedGame is null) return "请先选择游戏。";
            if (!_localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? profile)) return "尚未保存启动入口和存档目录。";
            if (IsAutoSyncEnabled) return "已启用；游戏退出后会自动创建并上传存档快照。";
            if (!IsAutomaticSyncProfileReady(profile)) return "启动入口或存档目录尚未配置完成，请先在游戏详情中完成配置。";
            int rootCount = profile.EffectiveSaveRoots.Count(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase));
            return $"配置已就绪：{rootCount} 个存档目录，可直接启用自动同步。";
        }
    }
    public string RetentionCountText { get => _retentionCountText; set => SetField(ref _retentionCountText, value); }
    public string RetentionDaysText { get => _retentionDaysText; set => SetField(ref _retentionDaysText, value); }

    public CloudGame? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetField(ref _selectedGame, value))
            {
                if (_learningBefore is not null || _learningCancellation is not null)
                    ResetSaveLearningState(cancel: true);
                _resumeAutomaticSyncAfterConfiguration = false;
                IsAutoSyncEnabled = false;
                SelectedSaveLocationCandidate = null;
                SaveDirectory = string.Empty;
                AdditionalSaveRootPath = string.Empty;
                RegistrySaveKeyPath = string.Empty;
                AutoSnapshotProcessName = string.Empty;
                AutoSnapshotExecutablePath = string.Empty;
                SaveLocationCandidates.Clear();
                AdditionalSaveRoots.Clear();
                SelectedAdditionalSaveRoot = null;
                RegistrySaveRules.Clear();
                SelectedRegistrySaveRule = null;
                IsSaveDirectoryConfirmed = false;
                FileCount = 0;
                LogicalSizeText = "0 B";
                RestorePreviewText = "选择快照后可预览将恢复的文件数量与大小。";
                RetentionEnabled = false;
                RetentionCountText = "50";
                RetentionDaysText = "0";
                Snapshots.Clear();
                SelectedSnapshot = null;
                SelectedDiscoveredGame = null;
                InvalidateSavePreview("请选择当前游戏的存档目录并重新预览。");
                ApplyDiscoveredIdentity(value);
                GameSyncUiState? syncState = GetSelectedGameSyncState();
                SyncProgressText = syncState?.ProgressText ?? "等待立即备份";
                SyncProgressValue = syncState?.ProgressValue ?? 0;
                SyncSummaryText = string.IsNullOrWhiteSpace(syncState?.Summary) ? "暂无同步记录" : syncState.Summary;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchDisabledReason)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameHealthText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameLastSyncText)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameLastErrorText)));
                NotifySelectedGameSyncStateChanged();
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveConflict)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveConflictRemoteHeadSnapshotId)));
            }
        }
    }

    public CloudSnapshotSummary? SelectedSnapshot
    {
        get => _selectedSnapshot;
        set
        {
            if (SetField(ref _selectedSnapshot, value))
                RestorePreviewText = value is null
                    ? "选择快照后可预览将恢复的文件数量与大小。"
                    : "快照已变化，请重新加载恢复预览。";
        }
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
            if (SetField(ref _selectedSaveLocationCandidate, value))
            {
                InvalidateSavePreview("存档候选已变化，请重新预览。");
                if (value is not null) SaveDirectory = value.Path;
            }
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
                AutoSnapshotExecutablePath = value.ExecutablePath ?? string.Empty;
                AutoSnapshotProcessName = value.ProcessName ?? string.Empty;
                if (_isAddGameWizardActive)
                {
                    AddGameWizard.WorkingDirectory = value.InstallDirectory;
                    AddGameWizard.Arguments = string.Empty;
                    AddGameWizard.MonitoredProcessName = value.ProcessName ?? string.Empty;
                    AddGameWizard.LaunchValidated = false;
                    ClearPendingSaveConfiguration();
                }
            }
        }
    }

    public void BeginAddGameWizard()
    {
        _addGameWizardReturnState = new AddGameWizardReturnState(
            SelectedGame,
            SelectedDiscoveredGame,
            NewGameName,
            SaveDirectory,
            IsSaveDirectoryConfirmed,
            AutoSnapshotExecutablePath,
            AutoSnapshotProcessName,
            SaveLocationCandidates.ToArray(),
            SelectedSaveLocationCandidate,
            AdditionalSaveRoots.ToArray(),
            RegistrySaveRules.ToArray(),
            SaveRootPreviews.ToArray(),
            RegistrySavePreviews.ToArray(),
            _previewedSaveDirectory,
            _previewedSaveDirectoryFingerprint,
            SaveDirectoryPreviewText,
            FileCount,
            LogicalSizeText);
        _isAddGameWizardActive = true;
        AddGameWizard.Reset();
        NewGameName = string.Empty;
        SelectedDiscoveredGame = null;
        AutoSnapshotExecutablePath = string.Empty;
        AutoSnapshotProcessName = string.Empty;
        ClearPendingSaveConfiguration();
        StatusText = "请选择要添加的游戏来源。";
    }

    public void EndAddGameWizard(bool completed)
    {
        _isAddGameWizardActive = false;
        if (_learningBefore is not null || _learningCancellation is not null)
            CancelSaveLearning();
        if (!completed && _addGameWizardReturnState is { } state)
        {
            SelectedGame = null;
            SelectedGame = state.SelectedGame;
            SelectedDiscoveredGame = state.SelectedDiscoveredGame;
            NewGameName = state.NewGameName;
            AutoSnapshotExecutablePath = state.ExecutablePath;
            AutoSnapshotProcessName = state.ProcessName;
            SaveLocationCandidates.Clear();
            foreach (SaveLocationCandidate candidate in state.SaveLocationCandidates)
                SaveLocationCandidates.Add(candidate);
            SelectedSaveLocationCandidate = state.SelectedSaveLocationCandidate;
            SaveDirectory = state.SaveDirectory;
            _previewedSaveDirectory = state.PreviewedSaveDirectory;
            _previewedSaveDirectoryFingerprint = state.PreviewedSaveDirectoryFingerprint;
            SaveDirectoryPreviewText = state.SaveDirectoryPreviewText;
            FileCount = state.FileCount;
            LogicalSizeText = state.LogicalSizeText;
            IsSaveDirectoryConfirmed = state.IsSaveDirectoryConfirmed;
            AdditionalSaveRoots.Clear();
            foreach (SaveRootRule root in state.AdditionalSaveRoots)
                AdditionalSaveRoots.Add(root);
            RegistrySaveRules.Clear();
            foreach (RegistrySaveRule rule in state.RegistrySaveRules)
                RegistrySaveRules.Add(rule);
            SaveRootPreviews.Clear();
            foreach (SaveRootPreview preview in state.SaveRootPreviews)
                SaveRootPreviews.Add(preview);
            RegistrySavePreviews.Clear();
            foreach (RegistrySavePreview preview in state.RegistrySavePreviews)
                RegistrySavePreviews.Add(preview);
        }
        _addGameWizardReturnState = null;
    }

    private void ClearPendingSaveConfiguration()
    {
        if (_learningBefore is not null || _learningCancellation is not null)
            ResetSaveLearningState(cancel: true);
        SelectedSaveLocationCandidate = null;
        SaveLocationCandidates.Clear();
        SaveDirectory = string.Empty;
        AdditionalSaveRootPath = string.Empty;
        AdditionalSaveRoots.Clear();
        SelectedAdditionalSaveRoot = null;
        RegistrySaveKeyPath = string.Empty;
        RegistrySaveRules.Clear();
        RegistrySavePreviews.Clear();
        SelectedRegistrySaveRule = null;
        FileCount = 0;
        LogicalSizeText = "0 B";
        IsSaveDirectoryConfirmed = false;
        InvalidateSavePreview("请选择当前游戏的存档目录并重新预览。");
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
    public ICommand ReloadGamesCommand { get; }
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
    public ICommand CheckForUpdateCommand { get; }
    public ICommand DownloadUpdateCommand { get; }
    public ICommand CancelUpdateDownloadCommand { get; }
    public ICommand InstallUpdateCommand { get; }
    public ICommand ToggleStartupUpdateCheckCommand { get; }
    public ICommand CleanupLocalStorageCommand { get; }

    public event EventHandler? PasswordClearRequested;
    public event EventHandler? GameCreated;
    public event EventHandler<SyncConflictEventArgs>? SyncConflictDetected;
    public event EventHandler<UserConfirmationEventArgs>? UserConfirmationRequested;
    public event EventHandler? UpdateInstallationRequested;
    public event EventHandler<WindowsNotificationEventArgs>? WindowsNotificationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>密码仅暂存于内存，并由 PasswordBox 调用此方法传入。</summary>
    public void SetPassword(string password) => _password = password;


    /// <summary>启动时尝试恢复已保存的设备会话；失效 Token 只清理凭据，不影响本机文件。</summary>
    public async Task InitializeAsync()
    {
        if (!TryBeginAuthenticationOperation()) return;
        await LoadUpdatePreferencesAsync();
        await RefreshLocalStorageUsageAsync();
        _ = CheckForUpdatesOnStartupAsync();
        await _authenticationGate.WaitAsync();
        long generation = BeginSessionTransition();
        try
        {
            await _autoSyncCoordinator.DisableAllAsync();
            CancellationToken cancellationToken = _sessionLifetime.Token;
            string? savedServerAddress = await _serverAddressStore.ReadAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(savedServerAddress)) ServerAddress = savedServerAddress;
            await LoadManifestUpdateStatusAsync();
            Uri server = ParseServerUri();
            string? token = await _credentialStore.ReadAsync(CredentialTargets.ForDeviceToken(server), cancellationToken);
            if (string.IsNullOrWhiteSpace(token)) return;

            CloudAccountSession session = await _apiClient.GetSessionAsync(server, token, cancellationToken);
            string? persistedDeviceId = await _credentialStore.ReadAsync(
                CredentialTargets.StableDeviceId, cancellationToken);
            ValidateAccountIdentity(
                session.UserId,
                session.DeviceId,
                session.Username,
                IsValidDeviceId(persistedDeviceId) ? persistedDeviceId : null);
            if (generation != Volatile.Read(ref _sessionGeneration)) return;
            _authenticatedUserId = session.UserId;
            AuthenticatedUsername = session.Username;
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountUserId(server), session.UserId, cancellationToken);
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountName(server), session.Username, cancellationToken);
            await _credentialStore.SaveAsync(CredentialTargets.StableDeviceId, session.DeviceId, cancellationToken);
            IsAuthenticated = true;
            await ReloadGamesAsync(server, token);
            await ReloadDevicesAsync(server, token);
            await ReloadQuotaAsync(server, token);
            EnsureSessionCurrent(CaptureSessionStamp(server), server);
            StatusText = $"已恢复账号 {AuthenticatedUsername} 的登录状态。";
        }
        catch (GameSaveApiException exception) when (exception.StatusCode is 401 or 403)
        {
            Uri server = ParseServerUri();
            foreach (string target in new[]
                     {
                         CredentialTargets.ForDeviceToken(server),
                         CredentialTargets.ForAccountName(server),
                         CredentialTargets.ForAccountUserId(server)
                     })
            {
                try { await _credentialStore.DeleteAsync(target, CancellationToken.None); }
                catch (Exception cleanupException)
                {
                    _appLogger.Error(
                        "authentication.expired_credential_cleanup_failed",
                        cleanupException,
                        $"清理过期凭据 {target} 失败");
                }
            }
            ClearAuthenticatedUiState();
            StatusText = "登录状态已过期，请重新登录。";
        }
        catch (InvalidDataException exception)
        {
            bool authenticatedSessionWasEstablished = IsAuthenticated;
            _appLogger.Error(
                authenticatedSessionWasEstablished
                    ? "application.local_profile_corrupted"
                    : "application.session_identity_invalid",
                exception,
                authenticatedSessionWasEstablished
                    ? "本机游戏配置损坏，已停止自动同步"
                    : "服务端会话身份校验失败");
            try { await _autoSyncCoordinator.DisableAllAsync(); }
            catch (Exception cleanupException)
            {
                _appLogger.Error(
                    "application.corrupt_profile_monitor_cleanup_failed",
                    cleanupException,
                    "配置损坏后停止自动同步失败");
            }
            if (authenticatedSessionWasEstablished)
            {
                _localGameProfiles.Clear();
                IsAutoSyncEnabled = false;
                StatusText = exception.Message + " 云端数据未修改，请先修复或重新配置受影响的本机游戏。";
            }
            else
            {
                ClearAuthenticatedUiState();
                StatusText = exception.Message + " 请重新登录；本机存档和云端数据均未修改。";
            }
        }
        catch (OperationCanceledException) when (generation != Volatile.Read(ref _sessionGeneration)) { }
        catch (Exception exception)
        {
            _appLogger.Error("application.session_restore.failed", exception, "恢复本机登录会话失败");
            try { await _autoSyncCoordinator.DisableAllAsync(); }
            catch (Exception cleanupException)
            {
                _appLogger.Error(
                    "application.session_restore_monitor_cleanup_failed",
                    cleanupException,
                    "恢复会话失败后停止自动同步失败");
            }
            ClearAuthenticatedUiState();
            StatusText = "无法恢复登录状态；请检查服务端地址或重新登录。";
        }
        finally
        {
            _authenticationGate.Release();
            EndAuthenticationOperation();
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

    private async Task LoadUpdatePreferencesAsync()
    {
        try
        {
            _updatePreferences = await _updatePreferenceStore.LoadAsync(CancellationToken.None);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateCheckOnStartup)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateCheckOnStartupSelectionIndex)));
        }
        catch (Exception exception)
        {
            _appLogger.Error("update.preferences.read_failed", exception, "读取更新检查偏好失败");
            _updatePreferences = ClientUpdatePreferences.Default;
            UpdateStatusText = "无法读取更新偏好，本次仍可手动检查。";
        }
    }

    private async Task CheckForUpdatesOnStartupAsync()
    {
        if (!UpdateCheckOnStartup) return;
        if (_updatePreferences.LastCheckedAtUtc is { } lastChecked
            && DateTimeOffset.UtcNow - lastChecked < TimeSpan.FromHours(12))
        {
            if (SemanticVersion.TryParse(_updatePreferences.LastAvailableVersion, out SemanticVersion? known)
                && known is not null
                && known.CompareTo(SemanticVersion.Parse(ClientVersionText)) > 0)
                UpdateStatusText = $"最近检查发现 {known}；点击“检查更新”刷新下载信息。";
            else
                UpdateStatusText = $"最近检查于 {lastChecked.LocalDateTime:g}，当前没有已知新版本。";
            return;
        }
        await CheckForUpdatesAsync(false);
    }

    private async Task CheckForUpdatesAsync(bool manual)
    {
        if (IsUpdateBusy) return;
        IsUpdateBusy = true;
        UpdateDownloadProgress = 0;
        UpdateStatusText = "正在通过 GitHub 检查客户端更新…";
        try
        {
            bool includePrerelease = !string.Equals(ClientReleaseChannelText, "稳定", StringComparison.Ordinal);
            ClientUpdateRelease? release = await _clientUpdateService.CheckForUpdateAsync(
                ClientVersionText,
                includePrerelease,
                CancellationToken.None);
            _availableUpdate = release;
            _preparedUpdate = null;
            DateTimeOffset checkedAt = DateTimeOffset.UtcNow;
            _updatePreferences = _updatePreferences with
            {
                LastCheckedAtUtc = checkedAt,
                LastAvailableVersion = release?.Version
            };
            string preferenceWarning = string.Empty;
            try
            {
                await _updatePreferenceStore.SaveAsync(_updatePreferences, CancellationToken.None);
            }
            catch (Exception exception)
            {
                _appLogger.Error("update.preferences.write_failed", exception, "保存更新检查时间失败");
                preferenceWarning = "（检查结果未能写入本地）";
            }
            UpdateStatusText = (release is null
                ? $"当前已是最新版本；检查于 {checkedAt.LocalDateTime:g}。"
                : $"发现新版本 {release.Version}（{FormatBytes(release.Installer.Size)}，{(release.Prerelease ? "预发布" : "稳定版")}）。")
                + preferenceWarning;
        }
        catch (Exception exception)
        {
            _appLogger.Error("update.check.failed", exception, "检查客户端更新失败");
            UpdateStatusText = manual
                ? $"检查更新失败：{exception.Message}"
                : "后台检查更新失败；不影响存档功能，可稍后手动重试。";
        }
        finally
        {
            IsUpdateBusy = false;
            NotifyUpdateStateChanged();
        }
    }

    private async Task DownloadUpdateAsync()
    {
        if (_availableUpdate is null || IsUpdateBusy) return;
        _updateDownloadCancellation?.Dispose();
        _updateDownloadCancellation = new CancellationTokenSource();
        IsUpdateBusy = true;
        IsUpdateDownloading = true;
        UpdateDownloadProgress = 0;
        UpdateStatusText = $"正在下载并校验 {_availableUpdate.Version}…";
        var progress = new Progress<ClientUpdateDownloadProgress>(value =>
        {
            UpdateDownloadProgress = value.Percentage;
            UpdateStatusText = $"正在下载 {_availableUpdate.Version}：{value.Percentage:0}%（{FormatBytes(value.BytesReceived)} / {FormatBytes(value.TotalBytes)}）";
        });
        try
        {
            _preparedUpdate = await _clientUpdateService.DownloadUpdateAsync(
                _availableUpdate,
                ClientVersionText,
                progress,
                _updateDownloadCancellation.Token);
            UpdateDownloadProgress = 100;
            UpdateStatusText = $"版本 {_preparedUpdate.Release.Version} 已下载，SHA-256 校验通过；可以启动安装。";
        }
        catch (OperationCanceledException)
        {
            UpdateStatusText = "更新下载已取消，未保留未完成文件。";
        }
        catch (Exception exception)
        {
            _appLogger.Error("update.download.failed", exception, "下载客户端更新失败");
            UpdateStatusText = $"下载更新失败：{exception.Message}";
        }
        finally
        {
            IsUpdateDownloading = false;
            IsUpdateBusy = false;
            _updateDownloadCancellation.Dispose();
            _updateDownloadCancellation = null;
            NotifyUpdateStateChanged();
        }
    }

    private async Task ToggleStartupUpdateCheckAsync()
    {
        try
        {
            ClientUpdatePreferences updated = _updatePreferences with { CheckOnStartup = !UpdateCheckOnStartup };
            await _updatePreferenceStore.SaveAsync(updated, CancellationToken.None);
            _updatePreferences = updated;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateCheckOnStartup)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UpdateCheckOnStartupSelectionIndex)));
            UpdateStatusText = UpdateCheckOnStartup
                ? "已启用启动后后台检查；最多每 12 小时访问一次 GitHub。"
                : "已关闭启动后后台检查；仍可随时手动检查。";
        }
        catch (Exception exception)
        {
            _appLogger.Error("update.preferences.write_failed", exception, "保存更新检查偏好失败");
            UpdateStatusText = $"保存更新偏好失败：{exception.Message}";
        }
    }

    private async Task RefreshLocalStorageUsageAsync()
    {
        try
        {
            long usage = await _localStorageMaintenanceService.GetManagedUsageAsync(CancellationToken.None);
            LocalStorageUsageText = $"对象缓存、更新包和恢复事务共占用 {FormatBytes(usage)}。清理不会删除游戏原始存档或本机配置。";
        }
        catch (Exception exception)
        {
            _appLogger.Error("storage.usage_failed", exception, "统计本地缓存失败");
            LocalStorageUsageText = "暂时无法统计本地缓存；不会影响存档同步。";
        }
    }

    private async Task CleanupLocalStorageAsync()
    {
        if (IsLocalStorageBusy) return;
        IsLocalStorageBusy = true;
        try
        {
            await _syncQueue.WaitAsync(CancellationToken.None);
            LocalStorageMaintenanceResult result;
            try
            {
                result = await _localStorageMaintenanceService.CleanupAsync(CancellationToken.None);
            }
            finally { _syncQueue.Release(); }
            LocalStorageUsageText = $"清理完成：释放 {FormatBytes(result.FreedBytes)}，当前占用 {FormatBytes(result.AfterBytes)}；删除 {result.DeletedFiles} 个缓存文件和 {result.DeletedDirectories} 个旧目录。";
            StatusText = "本地可再生成缓存已安全清理；数据库、日志和游戏原始存档均未修改。";
        }
        catch (Exception exception) { ShowError("清理本地缓存失败", exception); }
        finally
        {
            IsLocalStorageBusy = false;
            if (CleanupLocalStorageCommand is AsyncCommand command) command.RaiseCanExecuteChanged();
        }
    }

    public bool TryLaunchPreparedUpdate()
    {
        if (_preparedUpdate is null) return false;
        try
        {
            _clientUpdateService.LaunchInstaller(_preparedUpdate);
            return true;
        }
        catch (Exception exception)
        {
            _appLogger.Error("update.install.launch_failed", exception, "启动更新安装器失败");
            UpdateStatusText = $"无法启动更新安装器：{exception.Message}";
            return false;
        }
    }

    private void NotifyUpdateStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanDownloadUpdate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CanInstallUpdate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasAvailableUpdate)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasPreparedUpdate)));
        foreach (ICommand command in new[] { CheckForUpdateCommand, DownloadUpdateCommand, CancelUpdateDownloadCommand, InstallUpdateCommand, ToggleStartupUpdateCheckCommand })
        {
            if (command is AsyncCommand asyncCommand) asyncCommand.RaiseCanExecuteChanged();
            else if (command is DelegateCommand delegateCommand) delegateCommand.RaiseCanExecuteChanged();
        }
    }
    /// <summary>切换导航页面；每个页面复用同一份同步与本地配置状态。</summary>
    private void NavigateTo(object? page)
    {
        string target = page?.ToString() ?? "首页";
        if (string.Equals(target, "同步中心", StringComparison.Ordinal))
            target = SelectedGame is null ? "游戏库" : "游戏详情";
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
        if (game is not CloudGame requested) return;
        CloudGame? selected = Games.FirstOrDefault(item =>
            string.Equals(item.GameId, requested.GameId, StringComparison.Ordinal));
        if (!IsAuthenticated || selected is null) return;
        SelectedGame = selected;
        string gameId = selected.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await RestoreLocalProfileAsync(server, token);
            EnsureSessionCurrent(session, server);
            await ReloadSnapshotsAsync(server, token, gameId);
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(gameId))
                StatusText = $"已选择 {selected.Name}；可以查看最近快照或点击启动。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (IsSelectedGame(gameId)) ShowError("加载游戏快照失败", exception);
        }
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
        if (!TryBeginAuthenticationOperation()) return;
        await _authenticationGate.WaitAsync();
        long generation = BeginSessionTransition();
        try
        {
            await DrainSyncQueueAsync();
            ClearAuthenticatedUiState();
            await _autoSyncCoordinator.DisableAllAsync();
            CancellationToken cancellationToken = _sessionLifetime.Token;
            Uri server = ParseServerUri();
            string deviceId = await _deviceIdentityProvider.GetOrCreateDeviceIdAsync(cancellationToken);
            AuthSession session = register
                ? await _apiClient.RegisterAsync(server, Username, _password, deviceId, Environment.MachineName, cancellationToken)
                : await _apiClient.LoginAsync(server, Username, _password, deviceId, Environment.MachineName, cancellationToken);
            ValidateAccountIdentity(session.UserId, session.DeviceId, Username.Trim(), deviceId);
            if (string.IsNullOrWhiteSpace(session.DeviceToken) || session.DeviceToken.Length > 8192)
                throw new InvalidDataException("服务端返回的设备 Token 不完整，已拒绝建立登录会话。");
            if (generation != Volatile.Read(ref _sessionGeneration)) return;
            await _credentialStore.SaveAsync(CredentialTargets.ForDeviceToken(server), session.DeviceToken, cancellationToken);
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountName(server), Username.Trim(), cancellationToken);
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountUserId(server), session.UserId, cancellationToken);
            await _credentialStore.SaveAsync(CredentialTargets.StableDeviceId, session.DeviceId, cancellationToken);
            await _serverAddressStore.SaveAsync(server.AbsoluteUri.TrimEnd('/'), cancellationToken);
            _authenticatedUserId = session.UserId;
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
            if (generation == Volatile.Read(ref _sessionGeneration))
                ShowError(register ? "注册失败" : "登录失败", exception);
        }
        finally
        {
            _password = string.Empty;
            PasswordClearRequested?.Invoke(this, EventArgs.Empty);
            _authenticationGate.Release();
            EndAuthenticationOperation();
        }
    }

    private async Task ReloadGamesAsync(Uri server, string token, string? preferredGameId = null)
    {
        SessionStamp session = CaptureSessionStamp(server);
        IReadOnlyList<CloudGame> games = await _apiClient.ListGamesAsync(server, token, session.CancellationToken);
        EnsureSessionCurrent(session, server);
        Games.Clear();
        foreach (CloudGame game in games) Games.Add(game);
        await _localGameProfileStore.ClaimLegacyAsync(
            GameSaveServerIdentity.CreateStableKey(server),
            session.UserId,
            games.Select(game => game.GameId).ToArray(),
            session.CancellationToken);
        EnsureSessionCurrent(session, server);
        SelectedGame = Games.FirstOrDefault(game => string.Equals(game.GameId, preferredGameId, StringComparison.Ordinal))
                       ?? Games.FirstOrDefault();
        await RestoreAutomaticSyncProfilesAsync(server, token, session);
        EnsureSessionCurrent(session, server);
        if (SelectedGame is not null)
        {
            await RestoreLocalProfileAsync(server, token);
            await ReloadSnapshotsAsync(server, token);
            await ReloadRetentionAsync(server, token);
        }
    }

    private async Task ReloadGamesFromUiAsync()
    {
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            string? preferredGameId = SelectedGame?.GameId;
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await ReloadGamesAsync(server, token, preferredGameId);
            EnsureSessionCurrent(session, server);
            StatusText = $"游戏库已刷新，共 {Games.Count} 个云端游戏。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            ShowError("刷新游戏库失败", exception);
        }
    }

    private async Task RestoreLocalProfileAsync(Uri server, string token)
    {
        string? gameId = SelectedGame?.GameId;
        if (gameId is null) return;
        SessionStamp session = CaptureSessionStamp(server);
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
            serverKey, session.UserId, gameId, session.CancellationToken);
        EnsureSessionCurrent(session, server);
        if (profile is null || !IsSelectedGame(gameId)) return;
        profile = await NormalizeGeneratedProfileRootsAsync(server, profile, session.CancellationToken);

        _localGameProfiles[profile.GameId] = profile;
        ApplyLocalProfileToSelectedGame(profile);
        RefreshGameRuntimeStatus();
        if (profile.AutoSnapshotEnabled)
        {
            await EnableAutomaticSyncAsync(server, token, gameId, profile, session.CancellationToken);
            EnsureSessionCurrent(session, server);
        }
        if (IsSelectedGame(gameId))
            IsAutoSyncEnabled = _autoSyncCoordinator.ActiveGameIds.Contains(gameId);
    }

    private void ApplyLocalProfileToSelectedGame(LocalGameProfile profile)
    {
        if (!IsSelectedGame(profile.GameId)) return;
        SaveLocationCandidate? candidate = string.IsNullOrWhiteSpace(profile.SaveDirectory)
            ? null
            : new SaveLocationCandidate(
                profile.SaveDirectory,
                Math.Clamp(profile.SaveDirectoryConfidence, 0, 100),
                profile.SaveDirectorySource,
                "来自当前账号在本机保存的游戏配置",
                0,
                0,
                null,
                [],
                !profile.UserConfirmed);
        SelectedSaveLocationCandidate = candidate;
        SaveDirectory = profile.SaveDirectory;
        AutoSnapshotProcessName = profile.ProcessName;
        AutoSnapshotExecutablePath = profile.ExecutablePath ?? string.Empty;
        AdditionalSaveRoots.Clear();
        foreach (SaveRootRule root in profile.EffectiveSaveRoots.Where(root =>
                     !string.Equals(root.RootId, "root", StringComparison.OrdinalIgnoreCase)
                     && !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)))
            AdditionalSaveRoots.Add(root);
        RegistrySaveRules.Clear();
        foreach (RegistrySaveRule rule in profile.EffectiveRegistrySaveRules) RegistrySaveRules.Add(rule);
        IsSaveDirectoryConfirmed = profile.UserConfirmed;
    }


    /// <summary>加载所有已启用的本机配置，让每个游戏分别监听对应的进程和存档目录。</summary>
    private async Task RestoreAutomaticSyncProfilesAsync(Uri server, string token, SessionStamp? expectedSession = null)
    {
        SessionStamp session = expectedSession ?? CaptureSessionStamp(server);
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        IReadOnlyList<LocalGameProfile> profiles = await _localGameProfileStore.ListAsync(
            serverKey, session.UserId, session.CancellationToken);
        EnsureSessionCurrent(session, server);
        _localGameProfiles.Clear();
        foreach (LocalGameProfile profile in profiles)
        {
            LocalGameProfile normalizedProfile = await NormalizeGeneratedProfileRootsAsync(server, profile, session.CancellationToken);
            _localGameProfiles[normalizedProfile.GameId] = normalizedProfile;
            if (!normalizedProfile.AutoSnapshotEnabled || !Games.Any(game => game.GameId == normalizedProfile.GameId)) continue;
            await EnableAutomaticSyncAsync(server, token, normalizedProfile.GameId, normalizedProfile, session.CancellationToken);
            EnsureSessionCurrent(session, server);
        }
        RefreshGameRuntimeStatus();
    }

    private static IReadOnlyList<string> GetMonitoredProcessNames(LocalGameProfile profile) =>
        GameProcessNameRules.GetEffectiveNames(profile.EffectiveLaunchProfile, profile.ProcessName);
    private async Task EnableAutomaticSyncAsync(
        Uri server,
        string token,
        string gameId,
        LocalGameProfile profile,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<SaveRootRule> fileRoots = GetAutomaticSyncFileRoots(profile);
        if (!IsAutomaticSyncProfileReady(profile)) return;
        long sessionGeneration = Volatile.Read(ref _sessionGeneration);
        await _autoSyncCoordinator.EnableAsync(
            gameId,
            new AutoSnapshotProfile(
                GetMonitoredProcessNames(profile),
                fileRoots.Select(root => root.Path).ToArray(),
                profile.EffectiveRegistrySaveRules.Count > 0),
            cancellationToken => RunAutomaticSyncAsync(
                server, token, profile.UserId, gameId, sessionGeneration, cancellationToken),
            cancellationToken,
            cancellationToken => CheckExternalLaunchFreshnessAsync(
                server, token, profile.UserId, gameId, sessionGeneration, cancellationToken));
    }

    private async Task CheckExternalLaunchFreshnessAsync(
        Uri server,
        string token,
        string userId,
        string gameId,
        long sessionGeneration,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
            LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
                GameSaveServerIdentity.CreateStableKey(server), userId, gameId, cancellationToken);
            if (profile is null) return;
            profile = await NormalizeGeneratedProfileRootsAsync(server, profile, cancellationToken);
            await PrepareRegistrySnapshotsAsync(
                server, userId, gameId, profile.EffectiveRegistrySaveRules, cancellationToken);
            CloudFreshnessResult freshness = await _cloudSyncService.CheckFreshnessAsync(
                server, token, userId, gameId, profile.EffectiveSaveRoots, cancellationToken);
            if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
            if (freshness.Status is CloudFreshnessStatus.UpToDate or CloudFreshnessStatus.LocalAhead) return;
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
                _activeConflicts[gameId] = new SyncConflictContext(
                    gameId,
                    freshness.RemoteHeadSnapshotId,
                    freshness.LocalFileCount,
                    freshness.LocalLogicalSize);
                if (IsSelectedGame(gameId))
                {
                    FileCount = freshness.LocalFileCount;
                    LogicalSizeText = FormatBytes(freshness.LocalLogicalSize);
                    StatusText = "检测到游戏从客户端外部启动，但云端和本机版本不安全。请立即退出游戏并在客户端处理冲突；本次退出不会覆盖云端版本。";
                }
                string gameName = Games.FirstOrDefault(game => game.GameId == gameId)?.Name ?? "游戏";
                RequestWindowsNotification(
                    $"{gameName} 存档版本需要处理",
                    "游戏已从外部启动，请立即退出并先在客户端处理云端存档冲突。",
                    WindowsNotificationKind.Warning);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveConflict)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveConflictRemoteHeadSnapshotId)));
                if (KeepLocalConflictCommand is AsyncCommand command) command.RaiseCanExecuteChanged();
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception exception)
        {
            _appLogger.Error("sync.external_launch_preflight.failed", exception, $"游戏 {gameId} 的外部启动检查失败");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
                if (IsSelectedGame(gameId))
                    StatusText = "游戏已在客户端外部运行，但无法安全完成云端版本检查。请退出游戏并在客户端重新启动，避免产生分叉进度。";
                string gameName = Games.FirstOrDefault(game => game.GameId == gameId)?.Name ?? "游戏";
                RequestWindowsNotification(
                    $"{gameName} 无法验证存档版本",
                    "请退出游戏并从 GameSave Manager 重新启动，确认云端存档已拉取。",
                    WindowsNotificationKind.Warning);
            });
        }
    }

    private async Task RunAutomaticSyncAsync(
        Uri server,
        string token,
        string userId,
        string gameId,
        long sessionGeneration,
        CancellationToken cancellationToken)
    {
        try
        {
            LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
                GameSaveServerIdentity.CreateStableKey(server), userId, gameId, cancellationToken);
            if (!IsOperationSessionCurrent(userId, sessionGeneration))
                throw new OperationCanceledException("账号会话已经变化。", cancellationToken);
            if (profile is null) throw new InvalidOperationException("未找到该游戏的本机同步配置。");
            profile = await NormalizeGeneratedProfileRootsAsync(server, profile, cancellationToken);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, userId, gameId, profile,
                SnapshotTrigger.GameExit, "游戏退出自动同步", false, cancellationToken,
                expectedSessionGeneration: sessionGeneration);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
                ApplySyncResult(gameId, result);
            });
        }
        catch (OperationCanceledException) { }
        catch (DestructiveSnapshotChangeException exception)
        {
            _appLogger.Error("sync.automatic.destructive_change_blocked", exception, $"游戏 {gameId} 的自动同步因大量删除被阻止");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
                SetGameSyncError(gameId, exception.Message);
                if (IsSelectedGame(gameId)) StatusText = exception.Message + " 请检查本机存档后使用手动同步确认。";
                string gameName = Games.FirstOrDefault(game => game.GameId == gameId)?.Name ?? "游戏";
                RequestWindowsNotification($"{gameName} 自动备份已暂停", exception.Message, WindowsNotificationKind.Warning);
            });
        }
        catch (Exception exception)
        {
            _appLogger.Error("sync.automatic.failed", exception, $"游戏 {gameId} 的自动同步失败");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
                SetGameSyncError(gameId, $"自动同步失败：{exception.Message}");
                if (SelectedGame?.GameId == gameId) StatusText = $"自动同步失败：{exception.Message}";
                string gameName = Games.FirstOrDefault(game => game.GameId == gameId)?.Name ?? "游戏";
                RequestWindowsNotification($"{gameName} 自动备份失败", exception.Message, WindowsNotificationKind.Error);
            });
            throw;
        }
    }

    private async Task<CloudSyncResult> RunQueuedSyncAsync(
        Uri server, string token, string userId, string gameId, LocalGameProfile profile, SnapshotTrigger trigger,
        string description, bool keepLocalOnConflict, CancellationToken cancellationToken,
        bool allowDestructiveChanges = false,
        long? expectedSessionGeneration = null)
    {
        long sessionGeneration = expectedSessionGeneration ?? Volatile.Read(ref _sessionGeneration);
        if (!IsOperationSessionCurrent(userId, sessionGeneration))
            throw new OperationCanceledException("账号会话已经变化。", cancellationToken);
        if (!profile.UserConfirmed) throw new InvalidOperationException("该游戏的存档目录尚未确认。");
        if (IsGameProcessRunningNow(profile))
            throw new InvalidOperationException("检测到游戏仍在运行。为避免跨写入时点生成混合快照，请完全退出游戏后再同步。");
        IReadOnlyList<SaveRootRule> roots = profile.EffectiveSaveRoots;
        if (roots.Count == 0 || roots.Any(root => !root.UserConfirmed || !Directory.Exists(root.Path)))
            throw new InvalidOperationException("该游戏存在未确认或已失效的存档目录。");

        using CancellationTokenSource linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_syncCancellations)
        {
            if (_syncCancellations.ContainsKey(gameId))
                throw new InvalidOperationException("该游戏已有同步任务正在运行或排队。");
            _syncCancellations[gameId] = linked;
        }
        bool queueAcquired = false;
        InvokeOnUi(() =>
        {
            if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
            IsSyncing = true;
            UpdateGameSyncState(gameId, state =>
            {
                state.IsSyncing = true;
                state.ProgressValue = 2;
                state.ProgressText = "正在等待同步任务…";
                state.Error = string.Empty;
            });
            if (IsSelectedGame(gameId))
            {
                SyncProgressValue = 2;
                SyncProgressText = "正在等待同步任务…";
            }
        });
        try
        {
            await _syncQueue.WaitAsync(linked.Token);
            queueAcquired = true;
            if (IsGameProcessRunningNow(profile))
                throw new InvalidOperationException("游戏在同步排队期间已经启动，本次备份已取消；请完全退出游戏后重试。");
            await PrepareRegistrySnapshotsAsync(
                server, userId, gameId, profile.EffectiveRegistrySaveRules, linked.Token);
            IProgress<CloudSyncProgress> progress = new Progress<CloudSyncProgress>(
                item => ReportSyncProgress(userId, sessionGeneration, gameId, item));
            return await _cloudSyncService.SyncAsync(server, token, userId, gameId, roots, trigger, description,
                linked.Token, keepLocalOnConflict, progress, allowDestructiveChanges);
        }
        finally
        {
            lock (_syncCancellations)
            {
                if (_syncCancellations.TryGetValue(gameId, out CancellationTokenSource? current)
                    && ReferenceEquals(current, linked))
                    _syncCancellations.Remove(gameId);
            }
            if (queueAcquired) _syncQueue.Release();
            InvokeOnUi(() =>
            {
                lock (_syncCancellations) IsSyncing = _syncCancellations.Count > 0;
                if (IsOperationSessionCurrent(userId, sessionGeneration))
                    UpdateGameSyncState(gameId, state => state.IsSyncing = false);
            });
        }
    }
    private void ReportSyncProgress(
        string userId,
        long sessionGeneration,
        string gameId,
        CloudSyncProgress progress)
    {
        void Apply()
        {
            if (!IsOperationSessionCurrent(userId, sessionGeneration)) return;
            GameSyncUiState state = GetOrCreateGameSyncState(gameId);
            double progressValue = progress.Stage switch
            {
                "准备" => 8,
                "扫描" => 22,
                "比对" => 38,
                "上传" when progress.Total > 0 => 38 + 52d * progress.Completed / progress.Total,
                "复核" => 92,
                "提交" => 94,
                "完成" => 100,
                _ => state.ProgressValue
            };
            UpdateGameSyncState(gameId, state =>
            {
                state.ProgressText = progress.Message;
                state.ProgressValue = progressValue;
            });
            if (!IsSelectedGame(gameId)) return;
            SyncProgressText = progress.Message;
            SyncProgressValue = progressValue;
        }
        if (System.Windows.Application.Current.Dispatcher.CheckAccess()) Apply();
        else _ = System.Windows.Application.Current.Dispatcher.InvokeAsync(Apply);
    }

    private LocalGameProfile GetRequiredLocalProfile(string gameId) =>
        _localGameProfiles.TryGetValue(gameId, out LocalGameProfile? profile)
            ? profile
            : throw new InvalidOperationException("请先保存并确认该游戏的本机存档配置。");
    private void ApplySyncResult(string gameId, CloudSyncResult result)
    {
        string gameName = Games.FirstOrDefault(game => game.GameId == gameId)?.Name ?? "游戏";
        string statusText = result.Status == CloudSyncStatus.RemoteAhead
            ? result.Message + " 请从时间线恢复云端快照，或明确选择保留本机版本。"
            : result.Message;
        string summary = result.Status == CloudSyncStatus.Success
            ? $"本次同步：{result.FileCount} 个文件，{FormatBytes(result.LogicalSize)}；上传 {result.UploadedObjectCount} 个内容对象；耗时 {result.Duration.TotalSeconds:0.0} 秒。"
            : $"同步未提交：检测到版本冲突；耗时 {result.Duration.TotalSeconds:0.0} 秒。可恢复云端版本或选择保留本机版本。";
        string progressText = result.Status == CloudSyncStatus.Success ? "同步完成" : "需要处理版本冲突";
        double progressValue = result.Status == CloudSyncStatus.Success
            ? 100
            : GetOrCreateGameSyncState(gameId).ProgressValue;
        if (result.Status == CloudSyncStatus.RemoteAhead)
            _activeConflicts[gameId] = new SyncConflictContext(
                gameId,
                result.RemoteHeadSnapshotId,
                result.FileCount,
                result.LogicalSize);
        else
            _activeConflicts.Remove(gameId);
        UpdateGameSyncState(gameId, state =>
        {
            state.Summary = summary;
            state.Error = result.Status == CloudSyncStatus.Success ? string.Empty : statusText;
            state.ProgressText = progressText;
            state.ProgressValue = progressValue;
        });
        if (SelectedGame?.GameId == gameId)
        {
            FileCount = result.FileCount;
            LogicalSizeText = FormatBytes(result.LogicalSize);
            StatusText = statusText;
            SyncSummaryText = summary;
            SyncProgressText = progressText;
            SyncProgressValue = progressValue;
            if (result.Status == CloudSyncStatus.RemoteAhead)
            {
                CurrentPage = "时间线";
                SyncConflictDetected?.Invoke(this, new SyncConflictEventArgs(gameId));
            }
        }
        if (KeepLocalConflictCommand is AsyncCommand keepLocalCommand) keepLocalCommand.RaiseCanExecuteChanged();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveConflict)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveConflictRemoteHeadSnapshotId)));
        RequestWindowsNotification(
            result.Status == CloudSyncStatus.Success ? $"{gameName} 备份完成" : $"{gameName} 需要处理同步冲突",
            statusText,
            result.Status == CloudSyncStatus.Success ? WindowsNotificationKind.Success : WindowsNotificationKind.Warning);
    }

    private GameSyncUiState? GetSelectedGameSyncState() =>
        SelectedGame is null || !_gameSyncUiStates.TryGetValue(SelectedGame.GameId, out GameSyncUiState? state)
            ? null
            : state;

    private GameSyncUiState GetOrCreateGameSyncState(string gameId)
    {
        if (!_gameSyncUiStates.TryGetValue(gameId, out GameSyncUiState? state))
        {
            state = new GameSyncUiState();
            _gameSyncUiStates[gameId] = state;
        }
        return state;
    }

    private void UpdateGameSyncState(string gameId, Action<GameSyncUiState> update)
    {
        GameSyncUiState state = GetOrCreateGameSyncState(gameId);
        update(state);
        if (SelectedGame?.GameId == gameId) NotifySelectedGameSyncStateChanged();
    }

    private void SetGameSyncError(string gameId, string message) =>
        UpdateGameSyncState(gameId, state =>
        {
            state.Error = message;
            state.ProgressText = "备份失败";
            state.IsSyncing = false;
        });

    private void NotifySelectedGameSyncStateChanged()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameLastSyncText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameLastErrorText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelectedGameSyncing)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameSyncProgressText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedGameSyncProgressValue)));
    }

    private void CancelSelectedGameSync()
    {
        string? gameId = SelectedGame?.GameId;
        if (gameId is null) return;
        CancelGameSync(gameId);
    }

    private void CancelGameSync(string gameId)
    {
        lock (_syncCancellations)
            if (_syncCancellations.TryGetValue(gameId, out CancellationTokenSource? cancellation))
                cancellation.Cancel();
    }

    private static void InvokeOnUi(Action action)
    {
        Dispatcher? dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null || dispatcher.CheckAccess()) action();
        else dispatcher.Invoke(action);
    }
    private string GetRegistryCacheDirectory(Uri server, string userId, string gameId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSaveManager", "registry",
        GameSaveServerIdentity.CreateStableKey(server),
        CreateStablePathSegment(userId),
        CreateStablePathSegment(gameId));

    private string GetRegistryCacheDirectory(Uri server, string gameId) =>
        GetRegistryCacheDirectory(server, RequireAuthenticatedUserId(), gameId);

    private static string CreateStablePathSegment(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant()[..24];

    private async Task<LocalGameProfile> NormalizeGeneratedProfileRootsAsync(
        Uri server,
        LocalGameProfile profile,
        CancellationToken cancellationToken)
    {
        if (profile.EffectiveRegistrySaveRules.Count == 0) return profile;
        if (string.IsNullOrWhiteSpace(profile.UserId))
            throw new InvalidDataException("注册表存档配置缺少账号归属，已停止加载以避免跨账号共用缓存。");
        string expectedRegistryPath = GetRegistryCacheDirectory(server, profile.UserId, profile.GameId);
        SaveRootRule[] roots = profile.EffectiveSaveRoots.Select(root =>
            string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)
                ? root with { Path = expectedRegistryPath }
                : root).ToArray();
        if (roots.Any(root => string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)))
        {
            SaveRootRule current = profile.EffectiveSaveRoots.First(root => string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase));
            if (string.Equals(current.Path, expectedRegistryPath, StringComparison.OrdinalIgnoreCase)) return profile;
        }
        else
        {
            roots = roots.Append(new SaveRootRule(
                "registry", expectedRegistryPath, ["*.json", "**/*.json"], [], SaveLocationSource.Manual, 100, true)).ToArray();
        }
        LocalGameProfile normalized = profile with { SaveRoots = roots };
        await _localGameProfileStore.SaveAsync(normalized, cancellationToken);
        return normalized;
    }

    private void DeleteGeneratedGameData(Uri server, string userId, string gameId)
    {
        string applicationDataRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSaveManager"));
        string registryRoot = Path.Combine(applicationDataRoot, "registry");
        string gameDataRoot = Path.Combine(registryRoot, GameSaveServerIdentity.CreateStableKey(server));
        string gameDataDirectory = Path.GetFullPath(GetRegistryCacheDirectory(server, userId, gameId));
        string relativePath = Path.GetRelativePath(gameDataRoot, gameDataDirectory);
        string[] relativeSegments = relativePath.Split(
            [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
            StringSplitOptions.RemoveEmptyEntries);
        if (Path.IsPathRooted(relativePath) || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal)
            || relativeSegments.Length != 2
            || relativeSegments.Any(segment => segment.Length != 24 || !segment.All(Uri.IsHexDigit)))
            throw new IOException("游戏本地数据目录越过了应用数据边界，已停止删除");
        EnsureNoReparsePointTraversal(applicationDataRoot, gameDataDirectory);
        if (Directory.Exists(gameDataDirectory)) Directory.Delete(gameDataDirectory, true);
    }

    private static void EnsureNoReparsePointTraversal(string root, string target)
    {
        string fullRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        string fullTarget = Path.TrimEndingDirectorySeparator(Path.GetFullPath(target));
        string relative = Path.GetRelativePath(fullRoot, fullTarget);
        if (Path.IsPathRooted(relative)
            || relative.Equals("..", StringComparison.Ordinal)
            || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("受控本地数据目录越过应用边界，已停止删除");

        string current = fullRoot;
        foreach (string segment in relative.Split(
                     [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                     StringSplitOptions.RemoveEmptyEntries).Prepend(string.Empty))
        {
            if (segment.Length > 0) current = Path.Combine(current, segment);
            if (Directory.Exists(current)
                && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("受控本地数据路径包含重解析点，已停止递归删除");
        }
    }

    private async Task PrepareRegistrySnapshotsAsync(
        Uri server,
        string userId,
        string gameId,
        IReadOnlyList<RegistrySaveRule>? rules = null,
        CancellationToken cancellationToken = default)
    {
        IReadOnlyList<RegistrySaveRule> effectiveRules = rules ?? RegistrySaveRules;
        if (effectiveRules.Count == 0) return;
        await _registrySaveSnapshotService.ExportAsync(
            GetRegistryCacheDirectory(server, userId, gameId), effectiveRules, cancellationToken);
    }

    private IReadOnlyList<SaveRootRule> GetConfiguredSaveRoots(Uri server, string gameId) =>
        BuildConfiguredSaveRoots(server, RequireAuthenticatedUserId(), gameId, IsSaveDirectoryConfirmed,
            AdditionalSaveRoots.ToArray(), RegistrySaveRules.ToArray());

    private IReadOnlyList<SaveRootRule> BuildConfiguredSaveRoots(
        Uri server,
        string userId,
        string gameId,
        bool userConfirmed,
        IReadOnlyList<SaveRootRule> additionalRoots,
        IReadOnlyList<RegistrySaveRule> registryRules)
    {
        if (string.IsNullOrWhiteSpace(SaveDirectory)) return [];
        SaveLocationSource source = SelectedSaveLocationCandidate?.Source ?? SaveLocationSource.Manual;
        int confidence = SelectedSaveLocationCandidate?.Confidence ?? (userConfirmed ? 100 : 0);
        var roots = new List<SaveRootRule>
        {
            SaveRootRule.CreateDefault(SaveDirectory, source, confidence, userConfirmed)
        };
        roots.AddRange(additionalRoots);
        SaveRootTopologyValidator.Validate(roots, GetCurrentGameIdentity().InstallDirectory);
        if (registryRules.Count > 0)
            roots.Add(new SaveRootRule("registry", GetRegistryCacheDirectory(server, userId, gameId),
                ["*.json", "**/*.json"], [], SaveLocationSource.Manual, 100, true));
        return roots;
    }

    private async Task AddAdditionalSaveRootAsync()
    {
        string? gameId = SelectedGame?.GameId;
        if (string.IsNullOrWhiteSpace(AdditionalSaveRootPath) || !Directory.Exists(AdditionalSaveRootPath))
            throw new InvalidOperationException("请填写存在的附加存档目录。");
        string path = Path.GetFullPath(AdditionalSaveRootPath);
        var topologyRoots = new List<SaveRootRule> { BuildPrimarySaveRootRule() };
        topologyRoots.AddRange(AdditionalSaveRoots);
        topologyRoots.Add(new SaveRootRule("pending", path, [], [], SaveLocationSource.Manual, 100, false));
        SaveRootTopologyValidator.Validate(topologyRoots, GetCurrentGameIdentity().InstallDirectory);
        string rootId;
        int index = 2;
        do rootId = $"root{index++}"; while (AdditionalSaveRoots.Any(root => string.Equals(root.RootId, rootId, StringComparison.OrdinalIgnoreCase)));
        AdditionalSaveRoots.Add(new SaveRootRule(rootId, path, [], [], SaveLocationSource.Manual, 100, false));
        InvalidateSavePreview("附加存档目录已变化，请重新预览完整配置并确认全部规则。");
        if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
        AdditionalSaveRootPath = string.Empty;
        StatusText = $"已添加待确认的附加存档目录：{path}。完整预览并确认后才会参与同步。";
        if (IsAuthenticated && gameId is not null)
            await SaveLocalProfileAsync(ParseServerUri(), autoSnapshotEnabled: false, expectedGameId: gameId);
    }

    private async Task AddRegistrySaveRuleAsync()
    {
        string? gameId = SelectedGame?.GameId;
        string keyPath = RegistrySaveKeyPath.Trim();
        if (!(keyPath.StartsWith("HKCU\\", StringComparison.OrdinalIgnoreCase) || keyPath.StartsWith("HKEY_CURRENT_USER\\", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("注册表存档仅支持 HKCU\\ 或 HKEY_CURRENT_USER\\ 路径。");
        if (RegistrySaveRules.Any(rule => string.Equals(rule.KeyPath, keyPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("该注册表路径已经在同步列表中。");
        int index = 1; string ruleId; do ruleId = $"registry{index++}"; while (RegistrySaveRules.Any(rule => string.Equals(rule.RuleId, ruleId, StringComparison.OrdinalIgnoreCase)));
        RegistrySaveRules.Add(new RegistrySaveRule(ruleId, keyPath, false));
        InvalidateSavePreview("注册表存档规则已变化，请重新预览并确认。");
        if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
        RegistrySaveKeyPath = string.Empty;
        StatusText = "已添加注册表存档路径；同步前会导出为受控 JSON。";
    }

    private async Task RemoveRegistrySaveRuleAsync(object? parameter)
    {
        string? gameId = SelectedGame?.GameId;
        RegistrySaveRule? requested = parameter as RegistrySaveRule ?? SelectedRegistrySaveRule;
        RegistrySaveRule target = requested is null
            ? throw new InvalidOperationException("请先选择要移除的注册表路径。")
            : RegistrySaveRules.FirstOrDefault(rule => string.Equals(rule.RuleId, requested.RuleId, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException("要移除的注册表路径已不在当前游戏配置中，请刷新后重试。");
        RegistrySaveRules.Remove(target);
        InvalidateSavePreview("注册表存档规则已变化，请重新预览并确认。");
        if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
        SelectedRegistrySaveRule = null;
        StatusText = "已移除注册表存档路径；保存或同步后会更新本机配置。";
    }
    private async Task RemoveAdditionalSaveRootAsync(object? parameter)
    {
        string? gameId = SelectedGame?.GameId;
        SaveRootRule? requested = parameter as SaveRootRule ?? SelectedAdditionalSaveRoot;
        SaveRootRule target = requested is null
            ? throw new InvalidOperationException("请先选择要移除的附加存档目录。")
            : AdditionalSaveRoots.FirstOrDefault(root => string.Equals(root.RootId, requested.RootId, StringComparison.OrdinalIgnoreCase))
              ?? throw new InvalidOperationException("要移除的附加存档目录已不在当前游戏配置中，请刷新后重试。");
        AdditionalSaveRoots.Remove(target);
        InvalidateSavePreview("附加存档目录已变化，请重新预览并确认全部规则。");
        if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
        SelectedAdditionalSaveRoot = null;
        StatusText = "已移除附加存档目录；保存或同步后会更新本机配置。";
        if (IsAuthenticated && gameId is not null)
            await SaveLocalProfileAsync(ParseServerUri(), autoSnapshotEnabled: false, expectedGameId: gameId);
    }

    public async Task UpdateAdditionalSaveRootRulesAsync(SaveRootRule updatedRoot)
    {
        string? gameId = SelectedGame?.GameId;
        int index = AdditionalSaveRoots.ToList().FindIndex(root =>
            string.Equals(root.RootId, updatedRoot.RootId, StringComparison.OrdinalIgnoreCase));
        if (index < 0) throw new InvalidOperationException("待更新的附加目录不存在。");
        AdditionalSaveRoots[index] = updatedRoot;
        SelectedAdditionalSaveRoot = updatedRoot;
        InvalidateSavePreview("包含/排除规则已变化，请重新预览完整配置并确认。");
        if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
        StatusText = "附加目录扫描规则已更新，重新预览前不会参与同步。";
    }
    private async Task<LocalGameProfile> SaveLocalProfileAsync(
        Uri server,
        bool autoSnapshotEnabled,
        SaveProfileConfirmationDraft? confirmationDraft = null,
        string? expectedGameId = null,
        long? expectedSessionGeneration = null)
    {
        long sessionGeneration = expectedSessionGeneration ?? Volatile.Read(ref _sessionGeneration);
        CloudGame selectedGame = SelectedGame ?? throw new InvalidOperationException("请先选择云端游戏");
        string gameId = selectedGame.GameId;
        if (expectedGameId is not null && !string.Equals(gameId, expectedGameId, StringComparison.Ordinal))
            throw new InvalidOperationException("操作期间当前游戏已变化，已停止保存以避免写错游戏配置。");
        bool userConfirmed = confirmationDraft?.UserConfirmed ?? IsSaveDirectoryConfirmed;
        IReadOnlyList<SaveRootRule> additionalRoots = confirmationDraft?.AdditionalRoots
            ?? AdditionalSaveRoots.ToArray();
        IReadOnlyList<RegistrySaveRule> registryRules = confirmationDraft?.RegistryRules
            ?? RegistrySaveRules.ToArray();
        GameIdentity identity = GetCurrentGameIdentity();
        string saveDirectory = SaveDirectory;
        string processName = AutoSnapshotProcessName;
        string executablePath = AutoSnapshotExecutablePath;
        SaveLocationSource source = SelectedSaveLocationCandidate?.Source ?? SaveLocationSource.Manual;
        int confidence = SelectedSaveLocationCandidate?.Confidence ?? (userConfirmed ? 100 : 0);
        string userId = RequireAuthenticatedUserId();
        bool addGameWizardActive = _isAddGameWizardActive;
        _localGameProfiles.TryGetValue(gameId, out LocalGameProfile? existingProfile);
        GameLaunchProfile? launchProfile = addGameWizardActive
            ? BuildPendingLaunchProfile()
            : _gameLaunchProfileMerger.Merge(existingProfile?.EffectiveLaunchProfile, identity,
                executablePath, processName);
        bool requireResolvedShortcut = addGameWizardActive;
        var configuredRoots = new List<SaveRootRule>();
        if (!string.IsNullOrWhiteSpace(saveDirectory))
        {
            configuredRoots.Add(SaveRootRule.CreateDefault(saveDirectory, source, confidence, userConfirmed));
            configuredRoots.AddRange(additionalRoots);
            SaveRootTopologyValidator.Validate(configuredRoots, identity.InstallDirectory);
            if (registryRules.Count > 0)
                configuredRoots.Add(new SaveRootRule("registry", GetRegistryCacheDirectory(server, userId, gameId),
                    ["*.json", "**/*.json"], [], SaveLocationSource.Manual, 100, true));
        }
        if (launchProfile is { TargetType: GameLaunchTargetType.Shortcut })
        {
            ShortcutResolution latestResolution = await _shortcutResolver.ResolveAsync(
                launchProfile.Target, CancellationToken.None);
            _shortcutResolutions[launchProfile.Target] = latestResolution;
            if (latestResolution.Resolved) _shortcutResolutionFailures.Remove(launchProfile.Target);
            else _shortcutResolutionFailures[launchProfile.Target] =
                latestResolution.FailureReason ?? "快捷方式解析失败。";
            requireResolvedShortcut = true;
        }
        EnsureUiOperationTarget(gameId, userId, sessionGeneration);
        string? validationError = ValidateLaunchProfile(launchProfile, requireResolvedShortcut);
        bool launchRequired = addGameWizardActive || autoSnapshotEnabled;
        if (validationError is not null && launchRequired) throw new InvalidOperationException(validationError);
        if (validationError is not null) launchProfile = null;
        await PrepareRegistrySnapshotsAsync(server, userId, gameId, registryRules);
        EnsureUiOperationTarget(gameId, userId, sessionGeneration);
        string? identityExecutablePath = launchProfile is null ? null : identity.ExecutablePath;
        if (launchProfile is { TargetType: GameLaunchTargetType.Shortcut }
            && _shortcutResolutions.TryGetValue(launchProfile.Target, out ShortcutResolution? shortcut)
            && shortcut.Resolved)
        {
            launchProfile = launchProfile with
            {
                ShortcutArguments = shortcut.Arguments,
                WorkingDirectory = string.IsNullOrWhiteSpace(launchProfile.WorkingDirectory)
                    ? shortcut.WorkingDirectory
                    : launchProfile.WorkingDirectory,
                MonitoredProcessNames = GameProcessNameRules.GetEffectiveNames(
                    launchProfile with
                    {
                        MonitoredProcessNames = launchProfile.MonitoredProcessNames
                            .Concat(string.IsNullOrWhiteSpace(shortcut.TargetPath)
                                ? []
                                : [shortcut.TargetPath])
                            .ToArray()
                    },
                    processName)
            };
            identityExecutablePath = shortcut.TargetPath;
        }
        LocalGameProfile profile = new(
            GameSaveServerIdentity.CreateStableKey(server), gameId,
            identity.Provider, identity.ProviderGameId, identity.InstallDirectory,
            saveDirectory, processName, executablePath,
            source, confidence,
            userConfirmed, autoSnapshotEnabled && userConfirmed && launchProfile is not null,
            configuredRoots, registryRules, identityExecutablePath, launchProfile, userId);
        await _localGameProfileStore.SaveAsync(profile, CancellationToken.None);
        EnsureUiOperationTarget(gameId, userId, sessionGeneration);
        _localGameProfiles[profile.GameId] = profile;
        RefreshGameRuntimeStatus();
        return profile;
    }

    private void EnsureUiOperationTarget(
        string gameId,
        string userId,
        long? expectedSessionGeneration = null)
    {
        if (!IsAuthenticated
            || (expectedSessionGeneration is not null
                && expectedSessionGeneration != Volatile.Read(ref _sessionGeneration))
            || !string.Equals(_authenticatedUserId, userId, StringComparison.Ordinal)
            || !IsSelectedGame(gameId))
            throw new InvalidOperationException("操作期间账号或游戏已变化，已停止写入以保证数据对应关系。");
    }

    private GameLaunchProfile BuildPendingLaunchProfile()
    {
        GameLaunchProfile profile = CreateLaunchProfile(GetCurrentGameIdentity())
            ?? throw new InvalidOperationException("未找到有效的游戏启动配置。");
        IReadOnlyList<string> confirmedProcesses = AddGameWizard.GetConfirmedMonitoredProcessNames();
        IReadOnlyList<string> monitoredProcesses = confirmedProcesses.Count > 0
            ? confirmedProcesses
            : string.IsNullOrWhiteSpace(AddGameWizard.MonitoredProcessName)
                ? profile.MonitoredProcessNames
                : [AddGameWizard.MonitoredProcessName.Trim()];
        return profile with
        {
            Arguments = string.IsNullOrWhiteSpace(AddGameWizard.Arguments) ? profile.Arguments : AddGameWizard.Arguments.Trim(),
            WorkingDirectory = string.IsNullOrWhiteSpace(AddGameWizard.WorkingDirectory)
                ? profile.WorkingDirectory
                : AddGameWizard.WorkingDirectory.Trim(),
            RunAsAdministrator = AddGameWizard.RunAsAdministrator,
            MonitoredProcessNames = monitoredProcesses
        };
    }

    private string? GetPendingLaunchProfileValidationError()
    {
        try { return ValidateLaunchProfile(BuildPendingLaunchProfile(), requireResolvedShortcut: true); }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException)
        {
            return exception.Message;
        }
    }

    private string? ValidateLaunchProfile(GameLaunchProfile? profile, bool requireResolvedShortcut)
    {
        if (profile is null || string.IsNullOrWhiteSpace(profile.Target)) return "请选择有效的启动入口。";
        if (profile.Arguments?.Length > 4096) return "游戏启动参数不能超过 4096 个字符。";
        if (!string.IsNullOrWhiteSpace(profile.WorkingDirectory) && !Directory.Exists(profile.WorkingDirectory))
            return "游戏工作目录不存在。";
        foreach (string processName in profile.MonitoredProcessNames)
        {
            string normalized = GameProcessNameRules.Normalize(processName);
            if (normalized.Length == 0 || GameProcessNameRules.IsUnsafeGenericName(normalized)
                || !string.Equals(Path.GetFileName(processName.Trim()), processName.Trim(), StringComparison.Ordinal)
                || processName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return $"监控进程名无效：{processName}";
        }
        if (profile.TargetType == GameLaunchTargetType.StoreUri)
        {
            if (!Uri.TryCreate(profile.Target, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme is not "steam" and not "com.epicgames.launcher"))
                return "平台启动地址无效。";
            return null;
        }
        string expectedExtension = profile.TargetType == GameLaunchTargetType.Shortcut ? ".lnk" : ".exe";
        if (!string.Equals(Path.GetExtension(profile.Target), expectedExtension, StringComparison.OrdinalIgnoreCase)
            || !File.Exists(profile.Target)) return $"启动入口必须是存在的 {expectedExtension} 文件。";
        if (profile.TargetType == GameLaunchTargetType.Shortcut && requireResolvedShortcut)
        {
            if (_shortcutResolutionFailures.TryGetValue(profile.Target, out string? failure)) return failure;
            if (!_shortcutResolutions.TryGetValue(profile.Target, out ShortcutResolution? resolution) || !resolution.Resolved)
                return "快捷方式尚未成功解析，请重新选择启动入口。";
            if (string.IsNullOrWhiteSpace(resolution.TargetPath) || !File.Exists(resolution.TargetPath))
                return "快捷方式指向的程序已经不存在，请重新选择启动入口。";
            if (!string.IsNullOrWhiteSpace(resolution.WorkingDirectory)
                && !Directory.Exists(resolution.WorkingDirectory))
                return "快捷方式的工作目录已经不存在，请重新创建或选择快捷方式。";
        }
        return null;
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
    private async Task RefreshAutomaticSyncConfigurationAsync(
        Uri server,
        string token,
        LocalGameProfile profile,
        CancellationToken cancellationToken = default)
    {
        await _autoSyncCoordinator.DisableAsync(profile.GameId);
        cancellationToken.ThrowIfCancellationRequested();
        if (profile.AutoSnapshotEnabled)
            await EnableAutomaticSyncAsync(server, token, profile.GameId, profile, cancellationToken);
    }

    private async Task<bool> SuspendAutomaticSyncForConfigurationAsync(string? expectedGameId = null)
    {
        string? gameId = SelectedGame?.GameId;
        if (expectedGameId is not null && !string.Equals(gameId, expectedGameId, StringComparison.Ordinal)) return false;
        if (gameId is null) return true;
        long sessionGeneration = Volatile.Read(ref _sessionGeneration);
        string userId = _authenticatedUserId;
        if (!IsOperationSessionCurrent(userId, sessionGeneration)) return false;
        bool active = IsAutoSyncEnabled
            || _autoSyncCoordinator.ActiveGameIds.Contains(gameId)
            || (_localGameProfiles.TryGetValue(gameId, out LocalGameProfile? profile)
                && profile.AutoSnapshotEnabled);
        _resumeAutomaticSyncAfterConfiguration |= active;
        await _autoSyncCoordinator.DisableAsync(gameId);
        if (!IsOperationSessionCurrent(userId, sessionGeneration) || !IsSelectedGame(gameId)) return false;
        IsAutoSyncEnabled = false;
        return true;
    }
    private async Task ReloadRetentionAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            EnsureUiOperationTarget(gameId, session.UserId, session.Generation);
            await ReloadRetentionAsync(server, token, gameId);
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(gameId)) StatusText = "快照保留策略已加载。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("加载保留策略失败", exception);
        }
    }

    private async Task ReloadRetentionAsync(Uri server, string token)
    {
        string? gameId = SelectedGame?.GameId;
        if (gameId is null) return;
        await ReloadRetentionAsync(server, token, gameId);
    }

    private async Task ReloadRetentionAsync(Uri server, string token, string gameId)
    {
        SessionStamp session = CaptureSessionStamp(server);
        CloudRetentionPolicy policy = await _apiClient.GetRetentionPolicyAsync(
            server, token, gameId, session.CancellationToken);
        EnsureSessionCurrent(session, server);
        if (IsSelectedGame(gameId)) ApplyRetentionPolicy(policy);
    }
    private async Task SaveRetentionAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            if (!int.TryParse(RetentionCountText, out int count) || count is < 1 or > 500)
                throw new InvalidOperationException("保留数量必须是 1 到 500 之间的整数");
            if (!int.TryParse(RetentionDaysText, out int days) || days is < 0 or > 3650)
                throw new InvalidOperationException("保留天数必须是 0 到 3650 之间的整数，0 表示不按时间清理");
            bool enabled = RetentionEnabled;
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            CloudRetentionPolicy policy = await _apiClient.UpdateRetentionPolicyAsync(
                server, token, gameId, enabled, count, days, CancellationToken.None);
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(gameId))
            {
                ApplyRetentionPolicy(policy);
                StatusText = policy.Enabled ? "快照自动保留策略已启用。" : "快照自动保留策略已关闭。";
            }
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("保存保留策略失败", exception);
        }
    }

    private async Task CleanupRetentionAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            string gameName = SelectedGame?.Name ?? gameId;
            if (!RequestConfirmation(
                    "确认立即清理历史快照",
                    $"将立即按照游戏“{gameName}”当前保存的保留策略删除历史快照。当前 HEAD 不会删除，但超出策略的历史版本删除后将无法从时间线恢复。",
                    "执行清理"))
            {
                StatusText = "已取消历史快照清理。";
                return;
            }
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await _syncQueue.WaitAsync(session.CancellationToken);
            CloudRetentionCleanupResult result;
            try
            {
                result = await _apiClient.CleanupRetentionAsync(
                    server, token, gameId, CancellationToken.None);
            }
            finally { _syncQueue.Release(); }
            EnsureSessionCurrent(session, server);
            await ReloadSnapshotsAsync(server, token, gameId);
            EnsureSessionCurrent(session, server);
            await ReloadQuotaAsync(server, token);
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(gameId))
                StatusText = $"保留策略执行完成，删除 {result.DeletedSnapshotCount} 个历史快照。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("执行保留策略失败", exception);
        }
    }

    private void ApplyRetentionPolicy(CloudRetentionPolicy policy)
    {
        RetentionEnabled = policy.Enabled;
        RetentionCountText = policy.RetentionCount.ToString();
        RetentionDaysText = policy.RetentionDays.ToString();
    }
    private async Task ReloadQuotaAsync()
    {
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await ReloadQuotaAsync(server, token);
            EnsureSessionCurrent(session, server);
            StatusText = "存储容量已刷新。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            ShowError("加载存储容量失败", exception);
        }
    }

    private async Task ReloadQuotaAsync(Uri server, string token)
    {
        SessionStamp session = CaptureSessionStamp(server);
        CloudQuota quota = await _apiClient.GetQuotaAsync(server, token, session.CancellationToken);
        EnsureSessionCurrent(session, server);
        QuotaUsageText = $"已用 {FormatBytes(quota.UsedBytes)} / {FormatBytes(quota.QuotaBytes)}，剩余 {FormatBytes(quota.RemainingBytes)}";
    }
    private async Task ReloadDevicesAsync()
    {
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await ReloadDevicesAsync(server, token);
            EnsureSessionCurrent(session, server);
            StatusText = $"已加载 {Devices.Count} 台登记设备。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            ShowError("加载设备失败", exception);
        }
    }

    private async Task ReloadDevicesAsync(Uri server, string token)
    {
        SessionStamp session = CaptureSessionStamp(server);
        IReadOnlyList<CloudDevice> devices = await _apiClient.ListDevicesAsync(server, token, session.CancellationToken);
        EnsureSessionCurrent(session, server);
        Devices.Clear();
        foreach (CloudDevice device in devices) Devices.Add(device);
        SelectedDevice = Devices.FirstOrDefault(device => device.Active);
    }

    private async Task RevokeDeviceAsync(object? parameter)
    {
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (parameter is not CloudDevice requestedDevice)
                throw new InvalidOperationException("撤销操作缺少明确的设备目标，已拒绝执行");
            string deviceId = requestedDevice.DeviceId;
            if (!Devices.Any(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal)))
                throw new InvalidOperationException("要撤销的设备已不在当前设备列表中，请刷新后重试");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await _apiClient.RevokeDeviceAsync(server, token, deviceId, CancellationToken.None);
            EnsureSessionCurrent(session, server);
            await ReloadDevicesAsync(server, token);
            EnsureSessionCurrent(session, server);
            StatusText = "设备 Token 已撤销。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            ShowError("撤销设备失败", exception);
        }
    }
    private async Task LoadLocalProfileFromUiAsync()
    {
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await RestoreLocalProfileAsync(server, token);
            EnsureSessionCurrent(session, server);
            StatusText = "本机游戏配置已加载。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            ShowError("加载本机配置失败", exception);
        }
    }

    private async Task SuggestSaveDirectoriesAsync()
    {
        ConfigurationOperationStamp operation = CaptureConfigurationOperationStamp();
        try
        {
            var progress = new Progress<SaveDetectionProgress>(item =>
            {
                if (IsConfigurationOperationCurrent(operation)) StatusText = item.Message;
            });
            IReadOnlyList<SaveLocationCandidate> candidates = await _saveLocationDetector.DetectAsync(
                operation.Identity, progress, operation.CancellationToken);
            if (!IsConfigurationOperationCurrent(operation)) return;
            SaveLocationCandidates.Clear();
            foreach (SaveLocationCandidate candidate in candidates) SaveLocationCandidates.Add(candidate);
            SelectedSaveLocationCandidate = null;
            StatusText = candidates.Count == 0 ? "未找到存档目录候选，可手动选择后预览确认。" : $"找到 {candidates.Count} 个候选目录；请选择并确认后才能同步。";
        }
        catch (Exception exception)
        {
            if (!IsConfigurationOperationCurrent(operation)) return;
            ShowError("检测存档目录失败", exception);
        }
    }

    private SaveRootRule BuildPrimarySaveRootRule() => SaveRootRule.CreateDefault(
        SaveDirectory, SelectedSaveLocationCandidate?.Source ?? SaveLocationSource.Manual, 100, false);

    private IReadOnlyList<SaveRootRule> BuildPreviewSaveRoots()
    {
        var roots = new List<SaveRootRule> { BuildPrimarySaveRootRule() };
        roots.AddRange(AdditionalSaveRoots.Select(root => root with { UserConfirmed = false }));
        SaveRootTopologyValidator.Validate(roots, GetCurrentGameIdentity().InstallDirectory);
        return roots;
    }

    private bool IsCurrentSavePreviewValid()
    {
        try
        {
            if (string.IsNullOrWhiteSpace(SaveDirectory) || !Directory.Exists(SaveDirectory)
                || string.IsNullOrWhiteSpace(_previewedSaveDirectoryFingerprint)
                || FileCount > GameSaveProtocolLimits.MaximumManifestFiles)
                return false;
            IReadOnlyList<SaveRootRule> roots = BuildPreviewSaveRoots();
            return roots.All(root => Directory.Exists(root.Path))
                && SaveRootPreviews.Count == roots.Count
                && SaveRootPreviews.All(preview => !preview.WasTruncated)
                && RegistrySavePreviews.Count == RegistrySaveRules.Count
                && RegistrySavePreviews.All(preview => preview.CanConfirm
                    && RegistrySaveRules.Any(rule => string.Equals(rule.RuleId, preview.Rule.RuleId, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(rule.KeyPath, preview.Rule.KeyPath, StringComparison.OrdinalIgnoreCase)))
                && string.Equals(
                    SaveProfileFingerprint.Create(roots, RegistrySaveRules),
                    _previewedSaveDirectoryFingerprint,
                    StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException or IOException)
        {
            return false;
        }
    }

    private void InvalidateSavePreview(string message)
    {
        if (SelectedGame is not null && (IsAutoSyncEnabled
            || _autoSyncCoordinator.ActiveGameIds.Contains(SelectedGame.GameId)))
            _resumeAutomaticSyncAfterConfiguration = true;
        IsSaveDirectoryConfirmed = false;
        _previewedSaveDirectory = null;
        _previewedSaveDirectoryFingerprint = null;
        SaveRootPreviews.Clear();
        RegistrySavePreviews.Clear();
        SaveDirectoryPreviewText = message;
        AddGameWizard.RefreshValidation();
        RaiseCommandStates();
    }
    private async Task PreviewSaveDirectoryAsync()
    {
        string? gameId = SelectedGame?.GameId;
        ConfigurationOperationStamp operation = CaptureConfigurationOperationStamp();
        try
        {
            if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
            if (!IsConfigurationOperationCurrent(operation)) return;
            if (string.IsNullOrWhiteSpace(SaveDirectory) || !Directory.Exists(SaveDirectory)) throw new InvalidOperationException("请选择存在的存档目录。");
            string saveDirectory = SaveDirectory;
            IReadOnlyList<SaveRootRule> roots = BuildPreviewSaveRoots();
            IReadOnlyList<RegistrySaveRule> registryRules = RegistrySaveRules.ToArray();
            string inputFingerprint = SaveProfileFingerprint.Create(roots, registryRules);
            SaveProfilePreview preview = await _saveDirectoryPreviewService.PreviewProfileAsync(
                roots, registryRules, operation.CancellationToken);
            IReadOnlyList<RegistrySavePreview> registryPreviews = await _registrySaveSnapshotService.PreviewAsync(
                registryRules, operation.CancellationToken);
            if (!IsConfigurationOperationCurrent(operation)) return;
            string currentFingerprint = SaveProfileFingerprint.Create(
                BuildPreviewSaveRoots(), RegistrySaveRules.ToArray());
            if (!string.Equals(currentFingerprint, inputFingerprint, StringComparison.Ordinal)) return;
            FileCount = preview.TotalFiles;
            LogicalSizeText = FormatBytes(preview.TotalSize);
            _previewedSaveDirectory = Path.GetFullPath(saveDirectory);
            _previewedSaveDirectoryFingerprint = preview.Fingerprint;
            SaveRootPreviews.Clear();
            foreach (SaveRootPreview root in preview.Roots) SaveRootPreviews.Add(root);
            RegistrySavePreviews.Clear();
            foreach (RegistrySavePreview registry in registryPreviews) RegistrySavePreviews.Add(registry);
            string[] allWarnings = preview.Warnings.Concat(registryPreviews
                .Where(item => !item.CanConfirm)
                .Select(item => $"{item.Rule.RuleId}：{item.Summary}"))
                .ToArray();
            string warnings = allWarnings.Length == 0 ? string.Empty : " 警告：" + string.Join("；", allWarnings);
            bool truncated = preview.Roots.Any(item => item.WasTruncated);
            SaveDirectoryPreviewText = $"{preview.Roots.Count} 个目录，{(truncated ? "至少 " : "共 ")}{preview.TotalFiles} 个匹配文件、{FormatBytes(preview.TotalSize)}；最近修改：{preview.LatestWriteTimeUtc?.ToLocalTime():g}。" + warnings;
            StatusText = "完整存档配置预览完成；确认后所有目录和规则才允许同步。";
            AddGameWizard.RefreshValidation();
            RaiseCommandStates();
        }
        catch (Exception exception)
        {
            if (!IsConfigurationOperationCurrent(operation)) return;
            InvalidateSavePreview("预览失败，请修正目录或扫描规则后重试。");
            ShowError("预览存档目录失败", exception);
        }
    }
    private async Task ConfirmSaveDirectoryAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (!IsCurrentSavePreviewValid())
                throw new InvalidOperationException("当前存档目录或扫描规则尚未完成预览，请先检查内容。");
            SaveRootRule[] confirmedAdditionalRoots = AdditionalSaveRoots
                .Select(root => root with { UserConfirmed = true }).ToArray();
            RegistrySaveRule[] confirmedRegistryRules = RegistrySaveRules
                .Select(rule => rule with { UserConfirmed = true }).ToArray();
            var draft = new SaveProfileConfirmationDraft(
                true, confirmedAdditionalRoots, confirmedRegistryRules);
            if (SelectedGame is not null)
            {
                Uri server = ParseServerUri();
                operationServer = server;
                session = CaptureSessionStamp(server);
                bool desiredAutoSync = _resumeAutomaticSyncAfterConfiguration || IsAutoSyncEnabled
                    || (_localGameProfiles.TryGetValue(gameId!, out LocalGameProfile? existing)
                        && existing.AutoSnapshotEnabled);
                string token = desiredAutoSync ? await RequireDeviceTokenAsync(server) : string.Empty;
                EnsureSessionCurrent(session, server);
                EnsureUiOperationTarget(gameId!, session.UserId, session.Generation);
                LocalGameProfile savedProfile = await SaveLocalProfileAsync(
                    server, desiredAutoSync, draft, gameId, session.Generation);
                await RefreshAutomaticSyncConfigurationAsync(
                    server, token, savedProfile, session.CancellationToken);
                EnsureSessionCurrent(session, server);
                IsAutoSyncEnabled = _autoSyncCoordinator.ActiveGameIds.Contains(savedProfile.GameId);
                if (desiredAutoSync && !IsAutoSyncEnabled)
                    throw new InvalidOperationException("存档配置已保存，但自动同步监控未能重新启动。");
                _resumeAutomaticSyncAfterConfiguration = false;
            }
            AdditionalSaveRoots.Clear();
            foreach (SaveRootRule root in confirmedAdditionalRoots) AdditionalSaveRoots.Add(root);
            RegistrySaveRules.Clear();
            foreach (RegistrySaveRule rule in confirmedRegistryRules) RegistrySaveRules.Add(rule);
            IsSaveDirectoryConfirmed = true;
            StatusText = $"已确认完整存档配置：{SaveRootPreviews.Count} 个目录、{FileCount} 个文件，{LogicalSizeText}。现在可以同步。";
            AddGameWizard.RefreshValidation();
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            IsAutoSyncEnabled = SelectedGame is not null
                && _autoSyncCoordinator.ActiveGameIds.Contains(SelectedGame.GameId);
            ShowError("确认存档目录失败", exception);
        }
    }

    private async Task StartSaveLearningAsync()
    {
        ConfigurationOperationStamp operation = CaptureConfigurationOperationStamp();
        try
        {
            if (_learningCancellation is not null) throw new InvalidOperationException("存档学习已经开始，请先完成或取消当前学习。");
            GameIdentity game = operation.Identity;
            if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath)) throw new InvalidOperationException("请先配置游戏 EXE。");
            _learningCancellation = CancellationTokenSource.CreateLinkedTokenSource(operation.CancellationToken);
            _learningOperationStamp = operation;
            RaiseCommandStates();
            StatusText = "正在记录游戏运行前的文件元数据…";
            IReadOnlyList<FileMetadataSnapshot> before = await _runtimeSaveLearningService.CaptureBeforeAsync(
                game, _learningCancellation.Token);
            EnsureConfigurationOperationCurrent(operation);
            _learningBefore = before;
            RaiseCommandStates();
            GameLaunchResult launchResult = await LaunchGameAsync(game, _learningCancellation.Token);
            EnsureConfigurationOperationCurrent(operation);
            StatusText = launchResult.Warning is null
                ? "已记录文件元数据并确认游戏正在运行；保存并退出后点击完成学习。"
                : $"已记录文件元数据并发送启动请求，但{launchResult.Warning}";
        }
        catch (OperationCanceledException)
        {
            ResetSaveLearningState(cancel: false);
            if (IsConfigurationOperationCurrent(operation))
                StatusText = "存档学习已取消。";
        }
        catch (Exception exception)
        {
            ResetSaveLearningState(cancel: true);
            if (!IsConfigurationOperationCurrent(operation)) return;
            ShowError("启动存档学习失败", exception);
        }
    }

    private void CancelSaveLearning()
    {
        ResetSaveLearningState(cancel: true);
        StatusText = "存档学习已取消。";
    }

    private void ResetSaveLearningState(bool cancel)
    {
        if (cancel) _learningCancellation?.Cancel();
        _learningCancellation?.Dispose();
        _learningCancellation = null;
        _learningBefore = null;
        _learningOperationStamp = null;
        RaiseCommandStates();
    }

    private async Task CompleteSaveLearningAsync()
    {
        ConfigurationOperationStamp? operation = _learningOperationStamp;
        try
        {
            if (_learningBefore is null || operation is null) throw new InvalidOperationException("请先启动存档学习。");
            EnsureConfigurationOperationCurrent(operation);
            IReadOnlyList<FileMetadataSnapshot> before = _learningBefore;
            CancellationToken token = _learningCancellation?.Token ?? CancellationToken.None;
            if (operation.Identity is { } identity
                && CreateLaunchProfile(identity) is { } launchProfile
                && GameProcessNameRules.GetEffectiveNames(launchProfile, identity.ProcessName)
                    .Any(name => SnapshotRunningProcessNames().Contains(GameProcessNameRules.Normalize(name))))
                throw new InvalidOperationException("游戏仍在运行。请先正常保存并完全退出游戏，再点击完成学习。");
            await Task.Delay(TimeSpan.FromSeconds(5), token);
            EnsureConfigurationOperationCurrent(operation);
            IReadOnlyList<SaveLocationCandidate> candidates = await _runtimeSaveLearningService.DetectChangesAsync(
                operation.Identity,
                before,
                new Progress<SaveDetectionProgress>(item =>
                {
                    if (IsConfigurationOperationCurrent(operation)) StatusText = item.Message;
                }),
                token);
            EnsureConfigurationOperationCurrent(operation);
            SaveLocationCandidates.Clear();
            foreach (SaveLocationCandidate candidate in candidates) SaveLocationCandidates.Add(candidate);
            SelectedSaveLocationCandidate = null;
            ResetSaveLearningState(cancel: false);
            StatusText = $"学习完成：找到 {candidates.Count} 个候选目录，仍需用户确认。";
        }
        catch (OperationCanceledException)
        {
            ResetSaveLearningState(cancel: false);
            if (operation is null || IsConfigurationOperationCurrent(operation))
                StatusText = "存档学习已取消。";
        }
        catch (Exception exception)
        {
            ResetSaveLearningState(cancel: true);
            if (operation is not null && !IsConfigurationOperationCurrent(operation)) return;
            ShowError("完成存档学习失败", exception);
        }
    }
    private async Task DiscoverGamesAsync()
    {
        ConfigurationOperationStamp operation = CaptureConfigurationOperationStamp();
        try
        {
            StatusText = "正在扫描 Steam、Epic 与 GOG 的本机安装信息…";
            IReadOnlyList<DiscoveredGame> games = await _gameDiscoveryService.DiscoverAsync(
                operation.CancellationToken);
            if (!IsConfigurationOperationCurrent(operation)) return;
            DiscoveredGames.Clear();
            foreach (DiscoveredGame game in games) DiscoveredGames.Add(game);
            if (_isAddGameWizardActive)
            {
                SelectedDiscoveredGame = null;
                SelectedDiscoveredGame = DiscoveredGames.FirstOrDefault();
            }
            else
            {
                ApplyDiscoveredIdentity(SelectedGame);
                SelectedDiscoveredGame ??= DiscoveredGames.FirstOrDefault();
            }
            StatusText = $"已发现 {DiscoveredGames.Count} 个安装游戏。已自动关联当前云端游戏的 EXE；存档目录仍需手动确认。";
        }
        catch (Exception exception)
        {
            if (!IsConfigurationOperationCurrent(operation)) return;
            ShowError("扫描本机游戏失败", exception);
        }
    }

    private async Task CreateGameAsync()
    {
        CloudGame? createdGame = null;
        Uri? operationServer = null;
        SessionStamp? operationSession = null;
        try
        {
            if (!_isAddGameWizardActive || !AddGameWizard.IsFinalConfigurationValid)
                throw new InvalidOperationException("添加向导尚未完成全部启动与存档验证，禁止创建云端游戏。");
            string normalizedName = NewGameName.Trim();
            string pendingSaveDirectory = SaveDirectory;
            bool pendingDirectoryConfirmed = IsSaveDirectoryConfirmed;
            string pendingExecutablePath = AutoSnapshotExecutablePath;
            string pendingProcessName = AutoSnapshotProcessName;
            SaveLocationCandidate? pendingCandidate = SelectedSaveLocationCandidate;
            SaveRootRule[] pendingAdditionalRoots = AdditionalSaveRoots.ToArray();
            RegistrySaveRule[] pendingRegistryRules = RegistrySaveRules.ToArray();
            string discoveredProvider = SelectedDiscoveredGame?.Provider ?? GameIdentity.Custom;
            string provider = string.Equals(discoveredProvider, GameIdentity.Local, StringComparison.OrdinalIgnoreCase)
                ? GameIdentity.Custom
                : discoveredProvider;
            string? providerGameId = SelectedDiscoveredGame?.ProviderGameId;
            bool enableAutomaticBackup = AddGameWizard.EnableAutomaticBackup
                && AddGameWizard.LaunchValidated;
            if (Games.Any(game => string.Equals(game.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase)))
            {
                throw new InvalidOperationException("已添加同名游戏；同一账号下游戏名称不能重复。");
            }
            Uri server = ParseServerUri();
            operationServer = server;
            operationSession = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(operationSession, server);
            // LOCAL 仅用于客户端识别本地可执行文件，服务端创建云端游戏时应归入自定义游戏。
            try
            {
                createdGame = await _apiClient.CreateGameAsync(
                    server, token, normalizedName, provider, providerGameId, CancellationToken.None);
            }
            catch (Exception createFailure) when (IsAmbiguousWriteFailure(createFailure))
            {
                // 写请求可能已到达服务端但响应丢失；按账号内唯一名称核对，避免重复创建。
                try
                {
                    IReadOnlyList<CloudGame> currentGames = await _apiClient.ListGamesAsync(server, token, CancellationToken.None);
                    createdGame = currentGames.SingleOrDefault(item =>
                        string.Equals(item.Name.Trim(), normalizedName, StringComparison.OrdinalIgnoreCase));
                }
                catch { /* 保留原始创建错误。 */ }
                if (createdGame is null) throw new InvalidOperationException("创建请求失败且无法确认服务端是否已创建游戏，请刷新游戏库后重试。", createFailure);
            }
            EnsureSessionCurrent(operationSession, server);
            CloudGame game = createdGame;
            if (!Games.Any(item => string.Equals(item.GameId, game.GameId, StringComparison.Ordinal)))
                Games.Add(game);
            SelectedGame = Games.First(item => string.Equals(item.GameId, game.GameId, StringComparison.Ordinal));
            SelectedSaveLocationCandidate = pendingCandidate;
            SaveDirectory = pendingSaveDirectory;
            AutoSnapshotExecutablePath = pendingExecutablePath;
            AutoSnapshotProcessName = pendingProcessName;
            AdditionalSaveRoots.Clear();
            foreach (SaveRootRule root in pendingAdditionalRoots) AdditionalSaveRoots.Add(root);
            RegistrySaveRules.Clear();
            foreach (RegistrySaveRule rule in pendingRegistryRules) RegistrySaveRules.Add(rule);
            IsSaveDirectoryConfirmed = pendingDirectoryConfirmed;

            var refreshWarnings = new List<string>();
            try
            {
                if (pendingDirectoryConfirmed)
                {
                    LocalGameProfile profile = await SaveLocalProfileAsync(
                        server, enableAutomaticBackup, expectedGameId: game.GameId,
                        expectedSessionGeneration: operationSession.Generation);
                    await RefreshAutomaticSyncConfigurationAsync(
                        server, token, profile, operationSession.CancellationToken);
                    EnsureSessionCurrent(operationSession, server);
                    IsAutoSyncEnabled = enableAutomaticBackup
                        && _autoSyncCoordinator.ActiveGameIds.Contains(game.GameId);
                }
            }
            catch (Exception exception)
            {
                refreshWarnings.Add("本机保护配置保存失败，请在游戏详情重新确认");
                _appLogger.Error("game.create.local_setup_failed", exception, $"游戏 {game.GameId} 已创建，但本机配置未完成");
            }
            EnsureSessionCurrent(operationSession, server);
            try { await ReloadSnapshotsAsync(server, token, game.GameId); }
            catch (Exception exception)
            {
                refreshWarnings.Add("时间线暂未加载");
                _appLogger.Error("game.create.snapshots_refresh_failed", exception, "创建游戏后加载时间线失败");
            }
            EnsureSessionCurrent(operationSession, server);
            try { await ReloadRetentionAsync(server, token, game.GameId); }
            catch (Exception exception)
            {
                refreshWarnings.Add("保留策略暂未加载");
                _appLogger.Error("game.create.retention_refresh_failed", exception, "创建游戏后加载保留策略失败");
            }
            EnsureSessionCurrent(operationSession, server);
            try { await ReloadQuotaAsync(server, token); }
            catch (Exception exception)
            {
                refreshWarnings.Add("容量暂未刷新");
                _appLogger.Error("game.create.quota_refresh_failed", exception, "创建游戏后刷新容量失败");
            }
            EnsureSessionCurrent(operationSession, server);
            NewGameName = string.Empty;
            CurrentPage = "游戏详情";
            string warning = refreshWarnings.Count == 0 ? string.Empty : $" 注意：{string.Join("；", refreshWarnings)}。";
            StatusText = pendingDirectoryConfirmed && refreshWarnings.Count == 0
                ? $"已创建云端游戏：{game.Name}，存档配置已确认，现在可以立即备份。"
                : $"已创建云端游戏：{game.Name}。{warning}";
            GameCreated?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception exception)
        {
            if (operationSession is not null && operationServer is not null
                && !IsSessionCurrent(operationSession, operationServer)) return;
            if (createdGame is null) ShowError("创建游戏失败", exception);
            else
            {
                _appLogger.Error("game.create.post_create_failed", exception, $"游戏 {createdGame.GameId} 已创建，后续初始化失败");
                if (!Games.Any(game => string.Equals(game.GameId, createdGame.GameId, StringComparison.Ordinal))) Games.Add(createdGame);
                SelectedGame = Games.First(game => string.Equals(game.GameId, createdGame.GameId, StringComparison.Ordinal));
                CurrentPage = "游戏详情";
                StatusText = $"云端游戏“{createdGame.Name}”已经创建，但本机初始化未完成：{exception.Message}。请在详情页重新配置，客户端不会自动删除已创建的云端游戏。";
                GameCreated?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    /// <summary>删除云端游戏及全部云端快照，并移除这台电脑的对应同步配置。</summary>
    private async Task DeleteGameAsync(object? parameter)
    {
        Uri? operationServer = null;
        SessionStamp? operationSession = null;
        try
        {
            if (parameter is not CloudGame requestedGame)
                throw new InvalidOperationException("删除操作缺少明确的游戏目标，已拒绝执行");
            CloudGame targetGame = Games.FirstOrDefault(game =>
                    string.Equals(game.GameId, requestedGame.GameId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("要删除的游戏已不在当前游戏库中，请刷新后重试");
            Uri server = ParseServerUri();
            operationServer = server;
            operationSession = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(operationSession, server);
            string gameId = targetGame.GameId;
            string gameName = targetGame.Name;
            bool monitorWasActive = _autoSyncCoordinator.ActiveGameIds.Contains(gameId);
            _localGameProfiles.TryGetValue(gameId, out LocalGameProfile? profileBeforeDelete);
            await _autoSyncCoordinator.DisableAsync(gameId);
            EnsureSessionCurrent(operationSession, server);
            CancelGameSync(gameId);
            await _syncQueue.WaitAsync(operationSession.CancellationToken);
            try
            {
                try
                {
                    await _apiClient.DeleteGameAsync(server, token, gameId, CancellationToken.None);
                }
                catch
                {
                    bool deletionReachedServer = false;
                    try
                    {
                        IReadOnlyList<CloudGame> currentGames = await _apiClient.ListGamesAsync(server, token, CancellationToken.None);
                        deletionReachedServer = currentGames.All(game => !string.Equals(game.GameId, gameId, StringComparison.Ordinal));
                    }
                    catch { /* 无法消除网络写入歧义时，优先恢复原监控并保留本地状态。 */ }
                    if (!deletionReachedServer)
                    {
                        if (monitorWasActive && profileBeforeDelete is not null
                            && IsSessionCurrent(operationSession, server))
                        {
                            try { await EnableAutomaticSyncAsync(server, token, gameId, profileBeforeDelete); }
                            catch (Exception monitorFailure)
                            {
                                _appLogger.Error("game.delete.monitor_restore_failed", monitorFailure, $"恢复游戏 {gameId} 的自动监控失败");
                            }
                        }
                        throw;
                    }
                }
            }
            finally { _syncQueue.Release(); }
            string serverKey = GameSaveServerIdentity.CreateStableKey(server);
            var cleanupWarnings = new List<string>();
            try
            {
                await _localGameProfileStore.DeleteAsync(
                    serverKey, operationSession.UserId, gameId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                cleanupWarnings.Add("本机游戏配置稍后需要重新清理");
                _appLogger.Error("game.delete.local_profile_cleanup_failed", exception, $"清理游戏 {gameId} 的本机配置失败");
            }
            try
            {
                await _cloudSyncService.DeleteLocalStateAsync(
                    server, operationSession.UserId, gameId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                cleanupWarnings.Add("本机同步状态稍后需要重新清理");
                _appLogger.Error("game.delete.sync_state_cleanup_failed", exception, $"清理游戏 {gameId} 的本机同步状态失败");
            }
            try { DeleteGeneratedGameData(server, operationSession.UserId, gameId); }
            catch (Exception exception)
            {
                cleanupWarnings.Add("注册表临时缓存未能清理");
                _appLogger.Error("game.delete.generated_cache_cleanup_failed", exception, $"清理游戏 {gameId} 的生成缓存失败");
            }
            EnsureSessionCurrent(operationSession, server);
            _localGameProfiles.Remove(gameId);
            _gameSyncUiStates.Remove(gameId);
            _activeConflicts.Remove(gameId);
            _launchesInProgress.Remove(gameId);
            bool deletedSelectedGame = RemoveDeletedGameFromUi(gameId);
            if (deletedSelectedGame && SelectedGame is not null)
            {
                try
                {
                    await RestoreLocalProfileAsync(server, token);
                    await ReloadSnapshotsAsync(server, token);
                }
                catch (Exception exception)
                {
                    cleanupWarnings.Add("当前游戏资料刷新失败，可手动刷新");
                    _appLogger.Error("game.delete.selection_refresh_failed", exception, "删除游戏后刷新当前选择失败");
                }
            }
            EnsureSessionCurrent(operationSession, server);
            try { await ReloadQuotaAsync(server, token); }
            catch (Exception exception)
            {
                cleanupWarnings.Add("存储容量未能刷新");
                _appLogger.Error("game.delete.quota_refresh_failed", exception, "删除游戏后刷新容量失败");
            }
            EnsureSessionCurrent(operationSession, server);
            string warning = cleanupWarnings.Count == 0 ? string.Empty : $" 注意：{string.Join("；", cleanupWarnings)}。";
            StatusText = $"已删除游戏“{gameName}”、全部云端存档及这台电脑上的对应设置；本机原始存档未被删除。{warning}";
        }
        catch (Exception exception)
        {
            if (operationSession is not null && operationServer is not null
                && !IsSessionCurrent(operationSession, operationServer)) return;
            ShowError("删除游戏失败", exception);
        }
    }

    /// <summary>只移除已由服务端确认删除的明确目标；选择已切换时绝不触碰当前游戏的界面状态。</summary>
    private bool RemoveDeletedGameFromUi(string gameId)
    {
        bool deletedSelectedGame = string.Equals(SelectedGame?.GameId, gameId, StringComparison.Ordinal);
        CloudGame[] matches = Games.Where(game => string.Equals(game.GameId, gameId, StringComparison.Ordinal)).ToArray();
        foreach (CloudGame match in matches) Games.Remove(match);
        if (!deletedSelectedGame) return false;

        IsAutoSyncEnabled = false;
        SelectedGame = Games.FirstOrDefault();
        return true;
    }

    private bool IsSelectedGame(string gameId) =>
        string.Equals(SelectedGame?.GameId, gameId, StringComparison.Ordinal);

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
        await _authenticationGate.WaitAsync();
        BeginSessionTransition();
        var warnings = new List<string>();
        try
        {
            await DrainSyncQueueAsync();
            try { await _autoSyncCoordinator.DisableAllAsync(); }
            catch (Exception exception)
            {
                warnings.Add("后台监控未能完整停止");
                _appLogger.Error("authentication.logout_monitor_cleanup_failed", exception, "退出登录时停止自动同步失败");
            }

            Uri? server = null;
            try { server = ParseServerUri(); }
            catch (Exception exception)
            {
                warnings.Add("服务端地址无效，登录凭据未能定位");
                _appLogger.Error("authentication.logout_server_invalid", exception, "退出登录时解析服务端地址失败");
            }
            if (server is not null)
            {
                foreach (string target in new[]
                         {
                             CredentialTargets.ForDeviceToken(server),
                             CredentialTargets.ForAccountName(server),
                             CredentialTargets.ForAccountUserId(server)
                         })
                {
                    try { await _credentialStore.DeleteAsync(target, CancellationToken.None); }
                    catch (Exception exception)
                    {
                        warnings.Add("部分登录凭据未能清理");
                        _appLogger.Error("authentication.logout_credential_cleanup_failed", exception, $"清理凭据 {target} 失败");
                    }
                }
            }
        }
        catch (Exception exception)
        {
            warnings.Add("后台监控清理出现异常");
            _appLogger.Error("authentication.logout_cleanup_failed", exception, "退出登录清理失败");
        }
        finally
        {
            ClearAuthenticatedUiState();
            CurrentPage = "账户";
            StatusText = warnings.Count == 0
                ? "已退出登录；本机游戏存档文件不会被删除。"
                : $"已退出当前界面会话；{string.Join("；", warnings.Distinct())}，下次登录会重新校验。";
            _authenticationGate.Release();
        }
    }
    private async Task BuildManifestAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            StatusText = "正在完整扫描目录并计算 SHA-256…";
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            IReadOnlyList<SaveRootRule> saveRoots = GetConfiguredSaveRoots(server, gameId);
            IReadOnlyList<RegistrySaveRule> registryRules = RegistrySaveRules.ToArray();
            await PrepareRegistrySnapshotsAsync(
                server, session.UserId, gameId, registryRules, session.CancellationToken);
            IReadOnlyList<SnapshotFile> files = await _manifestBuilder.BuildAsync(
                saveRoots, session.CancellationToken);
            EnsureSessionCurrent(session, server);
            if (!IsSelectedGame(gameId)) return;
            FileCount = files.Count;
            LogicalSizeText = FormatBytes(files.Sum(file => file.Size));
            StatusText = "Manifest 已构建，Hash 缓存已写入本地 SQLite。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("扫描失败", exception);
        }
    }

    private async Task SyncAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string userId = session.UserId;
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            EnsureUiOperationTarget(gameId, userId, session.Generation);
            bool automaticSyncEnabled = IsAutoSyncEnabled;
            await SaveLocalProfileAsync(
                server, automaticSyncEnabled, expectedGameId: gameId,
                expectedSessionGeneration: session.Generation);
            CloudSyncResult result;
            try
            {
                result = await RunQueuedSyncAsync(server, token, userId, gameId, GetRequiredLocalProfile(gameId),
                    SnapshotTrigger.Manual, "手动同步", false, session.CancellationToken,
                    expectedSessionGeneration: session.Generation);
            }
            catch (DestructiveSnapshotChangeException destructive)
            {
                bool confirmed = RequestConfirmation(
                    "确认提交大量删除",
                    $"检测到本次存档比云端版本少 {destructive.RemovedFileCount} 个文件（{destructive.BaselineFileCount} → {destructive.CurrentFileCount}）。这可能是存档被误删、目录暂时不可访问或游戏主动清理。只有确认本机当前状态正确时才能继续。",
                    "仍然提交");
                if (!confirmed)
                {
                    StatusText = "已取消高风险同步；本机和云端数据均未修改。";
                    return;
                }
                EnsureSessionCurrent(session, server);
                result = await RunQueuedSyncAsync(server, token, userId, gameId, GetRequiredLocalProfile(gameId),
                    SnapshotTrigger.Manual, "手动同步（已确认大量删除）", false,
                    session.CancellationToken, true, session.Generation);
            }
            EnsureSessionCurrent(session, server);
            ApplySyncResult(gameId, result);
            await ReloadSnapshotsAsync(server, token, gameId);
            await ReloadQuotaAsync(server, token);
        }
        catch (OperationCanceledException)
        {
            if (gameId is null || session is null || operationServer is null
                || !IsSessionCurrent(session, operationServer)) return;
            SetGameSyncError(gameId, "同步已取消；下次同步会安全复用已上传内容。");
            StatusText = "同步已取消；下次同步会安全复用已上传内容。";
            RequestWindowsNotification("存档备份已取消", StatusText, WindowsNotificationKind.Warning);
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is not null) SetGameSyncError(gameId, $"同步失败：{ClientOperationError.FromException(exception).UserMessage}");
            RequestWindowsNotification("存档备份失败", ClientOperationError.FromException(exception).UserMessage, WindowsNotificationKind.Error);
            ShowError("同步失败", exception);
        }
    }

    private async Task KeepLocalConflictAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            if (!_activeConflicts.ContainsKey(gameId))
                throw new InvalidOperationException("当前游戏没有待处理且已核对的同步冲突，请先重新同步检查。 ");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string userId = session.UserId;
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            EnsureUiOperationTarget(gameId, userId, session.Generation);
            bool automaticSyncEnabled = IsAutoSyncEnabled;
            await SaveLocalProfileAsync(
                server, automaticSyncEnabled, expectedGameId: gameId,
                expectedSessionGeneration: session.Generation);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, userId, gameId, GetRequiredLocalProfile(gameId),
                SnapshotTrigger.Manual, "多设备冲突：保留本机版本", true,
                session.CancellationToken, true, session.Generation);
            EnsureSessionCurrent(session, server);
            ApplySyncResult(gameId, result);
            await ReloadSnapshotsAsync(server, token, gameId);
            await ReloadQuotaAsync(server, token);
        }
        catch (OperationCanceledException)
        {
            if (gameId is null || session is null || operationServer is null
                || !IsSessionCurrent(session, operationServer)) return;
            SetGameSyncError(gameId, "同步已取消。");
            StatusText = "同步已取消。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is not null) SetGameSyncError(gameId, $"保留本机版本失败：{ClientOperationError.FromException(exception).UserMessage}");
            ShowError("保留本机版本失败", exception);
        }
    }

    private async Task ReloadSnapshotsFromUiAsync()
    {
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await ReloadSnapshotsAsync(server, token, gameId);
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(gameId)) StatusText = $"已加载 {Snapshots.Count} 个快照版本。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("加载时间线失败", exception);
        }
    }

    private async Task ReloadSnapshotsAsync(Uri server, string token)
    {
        if (SelectedGame is null) return;
        await ReloadSnapshotsAsync(server, token, SelectedGame.GameId);
    }

    private async Task ReloadSnapshotsAsync(Uri server, string token, string gameId)
    {
        SessionStamp session = CaptureSessionStamp(server);
        IReadOnlyList<CloudSnapshotSummary> snapshots = await _apiClient.ListSnapshotsAsync(
            server, token, gameId, 100, session.CancellationToken);
        EnsureSessionCurrent(session, server);
        if (SelectedGame?.GameId != gameId) return;
        Snapshots.Clear();
        foreach (CloudSnapshotSummary snapshot in snapshots) Snapshots.Add(snapshot);
        SelectedSnapshot = Snapshots.FirstOrDefault();
        ApplyCloudPathSuggestions();
    }

    /// <summary>仅把云端历史路径当作待确认参考，不会绕过目录预览或自动写入本机配置。</summary>
    private void ApplyCloudPathSuggestions()
    {
        if (SelectedGame is null) return;
        bool hasUsableLocalProfile = _localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? localProfile)
                                     && localProfile.EffectiveSaveRoots
                                         .Where(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase))
                                         .All(root => Directory.Exists(root.Path));
        CloudSnapshotRoot[] fileRoots = Snapshots.FirstOrDefault()?.Roots?
            .Where(root => string.Equals(root.RootType, "FILE", StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(root.PathTemplate))
            .ToArray() ?? [];
        if (fileRoots.Length == 0) return;

        var resolved = fileRoots
            .Select(root => (Root: root, Path: _savePathTemplateService.Resolve(root.PathTemplate!)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path))
            .ToArray();
        if (resolved.Length == 0) return;

        foreach (var item in resolved)
        {
            if (SaveLocationCandidates.Any(candidate => string.Equals(candidate.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
                continue;
            bool exists = Directory.Exists(item.Path);
            SaveLocationCandidates.Add(new SaveLocationCandidate(
                item.Path!, Math.Clamp(item.Root.Confidence, 0, 100), SaveLocationSource.CloudHistory,
                exists
                    ? $"来自最近一次云端备份的路径记录（{item.Root.RootId}）：{item.Root.PathTemplate}"
                    : $"来自最近一次云端备份的预期路径（{item.Root.RootId}，当前尚不存在）：{item.Root.PathTemplate}",
                0, 0, null, [], true));
        }
        if (hasUsableLocalProfile) return;
        var primary = resolved.FirstOrDefault(item =>
            string.Equals(item.Root.RootId, "root", StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(item.Path));
        if (primary.Path is null) primary = resolved.FirstOrDefault(item => Directory.Exists(item.Path));
        if (primary.Path is null)
        {
            StatusText = "已显示云端备份记录的预期路径，但这些目录当前尚不存在；可先启动一次游戏或手动创建正确目录，再预览确认。";
            return;
        }
        SaveLocationCandidate selected = SaveLocationCandidates.First(candidate =>
            string.Equals(candidate.Path, primary.Path, StringComparison.OrdinalIgnoreCase));
        SelectedSaveLocationCandidate = selected;
        IsSaveDirectoryConfirmed = false;
        AdditionalSaveRoots.Clear();
        foreach (var item in resolved.Where(item =>
                     !string.Equals(item.Path, primary.Path, StringComparison.OrdinalIgnoreCase)
                     && Directory.Exists(item.Path)))
        {
            AdditionalSaveRoots.Add(new SaveRootRule(
                item.Root.RootId, item.Path!, item.Root.IncludePatterns ?? [], item.Root.ExcludePatterns ?? [],
                SaveLocationSource.CloudHistory, Math.Clamp(item.Root.Confidence, 0, 100), false));
        }
        StatusText = "已根据云端备份记录找到本机可能的存档路径；请预览并确认后再启用同步。";
    }

    /// <summary>恢复与同步共用 _syncQueue，避免恢复移动存档目录时与正在进行的（含自动）同步竞争同一目录。</summary>
    private async Task RestoreAsync()
    {
        string? gameId = SelectedGame?.GameId;
        string? snapshotId = SelectedSnapshot?.SnapshotId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            if (snapshotId is null) throw new InvalidOperationException("请从时间线选择要恢复的快照");
            if (!Snapshots.Any(snapshot => string.Equals(snapshot.SnapshotId, snapshotId, StringComparison.Ordinal)))
                throw new InvalidOperationException("要恢复的快照已不在当前时间线中，请刷新后重试。");
            if (string.IsNullOrWhiteSpace(SaveDirectory)) throw new InvalidOperationException("请先填写本地存档目录");
            if (_localGameProfiles.TryGetValue(gameId, out LocalGameProfile? runningProfile)
                && IsGameRunningNow(runningProfile))
                throw new InvalidOperationException("游戏仍在运行，已阻止恢复以避免覆盖正在写入的存档。");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string userId = session.UserId;
            IReadOnlyList<SaveRootRule> saveRoots = GetConfiguredSaveRoots(server, gameId);
            IReadOnlyList<RegistrySaveRule> registryRules = RegistrySaveRules.ToArray();
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(gameId)) StatusText = "正在等待同步任务空闲后开始恢复…";
            await _syncQueue.WaitAsync(session.CancellationToken);
            IReadOnlyList<RestoreResult> results;
            try
            {
                if (_localGameProfiles.TryGetValue(gameId, out LocalGameProfile? queuedProfile)
                    && IsGameProcessRunningNow(queuedProfile))
                    throw new InvalidOperationException("游戏在恢复排队期间已经启动，已阻止覆盖正在使用的存档。");
                if (IsSelectedGame(gameId)) StatusText = "正在下载、校验并安全恢复快照…";
                results = await _safeRestoreService.RestoreAsync(
                    server, token, userId, gameId, snapshotId, saveRoots, registryRules,
                    session.CancellationToken,
                    () =>
                    {
                        EnsureSessionCurrent(session, server);
                        if (_localGameProfiles.TryGetValue(gameId, out LocalGameProfile? currentProfile)
                            && IsGameProcessRunningNow(currentProfile))
                            throw new InvalidOperationException("游戏在快照下载期间已经启动，已在替换原存档前安全取消恢复。");
                    });
            }
            finally
            {
                _syncQueue.Release();
            }
            EnsureSessionCurrent(session, server);
            int backups = results.Count(item => item.SafetyBackupDirectory is not null);
            CloudFreshnessResult freshness;
            try
            {
                freshness = await _cloudSyncService.CheckFreshnessAsync(
                    server, token, userId, gameId, saveRoots, session.CancellationToken);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception verificationException)
            {
                _appLogger.Error(
                    "restore.post_verification_failed",
                    verificationException,
                    $"快照 {snapshotId} 已恢复，但云端版本复核失败");
                _activeConflicts[gameId] = new SyncConflictContext(
                    gameId, null, FileCount, 0);
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveConflict)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveConflictRemoteHeadSnapshotId)));
                if (KeepLocalConflictCommand is AsyncCommand uncertainConflictCommand)
                    uncertainConflictCommand.RaiseCanExecuteChanged();
                string backupText = backups == 0 ? string.Empty : $"，并保留 {backups} 份原存档安全备份";
                string completionWarning = $"快照 {snapshotId} 已恢复到 {results.Count} 个目录{backupText}，但未能复核云端 HEAD。已阻止启动和自动上传，请联网后重新检查版本。";
                if (IsSelectedGame(gameId))
                    StatusText = completionWarning;
                RequestWindowsNotification(
                    "存档已恢复，但版本待复核",
                    completionWarning,
                    WindowsNotificationKind.Warning);
                if (ClientOperationError.FromException(verificationException).Category == ErrorCategory.Authentication)
                    ShowError("存档已恢复，但登录状态复核失败", verificationException);
                return;
            }
            EnsureSessionCurrent(session, server);
            if (freshness.Status == CloudFreshnessStatus.UpToDate)
                _activeConflicts.Remove(gameId);
            else
                _activeConflicts[gameId] = new SyncConflictContext(
                    gameId,
                    freshness.RemoteHeadSnapshotId,
                    freshness.LocalFileCount,
                    freshness.LocalLogicalSize);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveConflict)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveConflictRemoteHeadSnapshotId)));
            if (KeepLocalConflictCommand is AsyncCommand keepLocalCommand) keepLocalCommand.RaiseCanExecuteChanged();
            if (IsSelectedGame(gameId))
            {
                string backupText = backups == 0
                    ? string.Empty
                    : $"；已保留 {backups} 份原存档安全备份";
                StatusText = freshness.Status == CloudFreshnessStatus.UpToDate
                    ? $"已恢复快照 {snapshotId} 到 {results.Count} 个存档目录{backupText}。"
                    : $"已恢复历史快照 {snapshotId} 到 {results.Count} 个存档目录{backupText}。启动游戏前，请选择“保留本机版本”创建新的云端 HEAD，或恢复当前云端 HEAD。";
            }
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("恢复存档失败", exception);
        }
    }


    private async Task LoadRestorePreviewAsync()
    {
        string? gameId = SelectedGame?.GameId;
        string? snapshotId = SelectedSnapshot?.SnapshotId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null || snapshotId is null)
                throw new InvalidOperationException("请先选择游戏和要预览的快照");
            if (!Snapshots.Any(snapshot => string.Equals(snapshot.SnapshotId, snapshotId, StringComparison.Ordinal)))
                throw new InvalidOperationException("要预览的快照已不在当前时间线中，请刷新后重试。");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            CloudSnapshotManifest manifest = await _apiClient.GetSnapshotAsync(
                server, token, gameId, snapshotId, session.CancellationToken);
            CloudApiResponseValidator.ValidateManifest(manifest, gameId, snapshotId);
            EnsureSessionCurrent(session, server);
            if (!IsSelectedGame(gameId) || !string.Equals(SelectedSnapshot?.SnapshotId, snapshotId, StringComparison.Ordinal)) return;
            long totalSize = manifest.Files.Sum(file => file.Size);
            string examples = string.Join("、", manifest.Files.Take(3).Select(file => file.RelativePath));
            RestorePreviewText = $"将恢复 {manifest.Files.Count} 个文件，共 {FormatBytes(totalSize)}。" +
                (string.IsNullOrWhiteSpace(examples) ? string.Empty : $" 示例：{examples}");
            StatusText = "恢复预览已加载；真正恢复前仍会创建安全备份并逐文件校验。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("加载恢复预览失败", exception);
        }
    }

    private async Task ExportSnapshotAsync()
    {
        CloudGame? game = SelectedGame;
        CloudSnapshotSummary? snapshot = SelectedSnapshot;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (game is null || snapshot is null)
                throw new InvalidOperationException("请先选择游戏和要导出的快照");
            if (!Games.Any(item => string.Equals(item.GameId, game.GameId, StringComparison.Ordinal))
                || !Snapshots.Any(item => string.Equals(item.SnapshotId, snapshot.SnapshotId, StringComparison.Ordinal)))
                throw new InvalidOperationException("要导出的游戏或快照已不在当前列表中，请刷新后重试。");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string name = string.Concat(game.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            string destination = GetAvailableExportPath(
                downloads,
                $"{name}-{snapshot.LocalCreateTime:yyyyMMdd-HHmmss}",
                ".zip");
            if (IsSelectedGame(game.GameId)) StatusText = "正在下载并校验快照内容，然后导出 ZIP…";
            await _syncQueue.WaitAsync(session.CancellationToken);
            string exported;
            try
            {
                exported = await _snapshotExportService.ExportAsync(
                    server, token, game.GameId, snapshot.SnapshotId, destination,
                    session.CancellationToken);
            }
            finally { _syncQueue.Release(); }
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(game.GameId)) StatusText = $"快照已导出到：{exported}";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (game is null || IsSelectedGame(game.GameId)) ShowError("导出快照失败", exception);
        }
    }

    private static string GetAvailableExportPath(string directory, string fileNameWithoutExtension, string extension)
    {
        string candidate = Path.Combine(directory, fileNameWithoutExtension + extension);
        for (int suffix = 2; File.Exists(candidate); suffix++)
            candidate = Path.Combine(directory, $"{fileNameWithoutExtension}-{suffix}{extension}");
        return candidate;
    }
    /// <summary>删除已明确确认的历史快照；服务端会拒绝删除当前同步 HEAD。</summary>
    private async Task DeleteSnapshotAsync()
    {
        string? gameId = SelectedGame?.GameId;
        string? snapshotId = SelectedSnapshot?.SnapshotId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            if (snapshotId is null) throw new InvalidOperationException("请从时间线选择要删除的历史快照");
            if (!Snapshots.Any(snapshot => string.Equals(snapshot.SnapshotId, snapshotId, StringComparison.Ordinal)))
                throw new InvalidOperationException("要删除的快照已不在当前时间线中，请刷新后重试。");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await _syncQueue.WaitAsync(session.CancellationToken);
            try
            {
                await _apiClient.DeleteSnapshotAsync(
                    server, token, gameId, snapshotId, CancellationToken.None);
            }
            finally { _syncQueue.Release(); }
            EnsureSessionCurrent(session, server);
            await ReloadSnapshotsAsync(server, token, gameId);
            EnsureSessionCurrent(session, server);
            await ReloadQuotaAsync(server, token);
            EnsureSessionCurrent(session, server);
            if (IsSelectedGame(gameId))
                StatusText = $"已删除历史快照 {snapshotId}；未被其他快照引用的内容将按云端清理策略回收。";
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("删除历史快照失败", exception);
        }
    }

    public async Task SetAutoSnapshotExecutablePathAsync(string executablePath)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return;
        string? gameId = SelectedGame?.GameId;
        long sessionGeneration = Volatile.Read(ref _sessionGeneration);
        string previousExecutablePath = AutoSnapshotExecutablePath;
        string previousProcessName = AutoSnapshotProcessName;
        try
        {
            AutoSnapshotExecutablePath = executablePath;
            AutoSnapshotProcessName = Path.GetFileName(executablePath);
            if (gameId is not null)
                await SaveLocalProfileAsync(
                    ParseServerUri(), IsAutoSyncEnabled, expectedGameId: gameId,
                    expectedSessionGeneration: sessionGeneration);
        }
        catch (Exception exception)
        {
            if (gameId is null
                || !IsOperationSessionCurrent(_authenticatedUserId, sessionGeneration)
                || !IsSelectedGame(gameId)) return;
            AutoSnapshotExecutablePath = previousExecutablePath;
            AutoSnapshotProcessName = previousProcessName;
            ShowError("保存游戏启动入口失败", exception);
        }
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
        ConfigurationOperationStamp operation = CaptureConfigurationOperationStamp();
        string fullPath = Path.GetFullPath(executablePath);
        GameIdentity identity;
        if (string.Equals(Path.GetExtension(fullPath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            ShortcutResolution resolution = await _shortcutResolver.ResolveAsync(
                fullPath, operation.CancellationToken);
            if (resolution.Resolved && resolution.TargetPath is { Length: > 0 } target)
            {
                GameIdentity resolvedIdentity = await _executableGameIdentityFactory.CreateAsync(
                    target, operation.CancellationToken);
                identity = resolvedIdentity with { ExecutablePath = fullPath };
                _shortcutResolutionFailures.Remove(fullPath);
                _shortcutResolutions[fullPath] = resolution;
            }
            else
            {
                identity = new GameIdentity(Path.GetFileNameWithoutExtension(fullPath), GameIdentity.Local, null,
                    Path.GetDirectoryName(fullPath) ?? string.Empty, fullPath, null);
                _shortcutResolutionFailures[fullPath] = resolution.FailureReason ?? "快捷方式解析失败。";
                _shortcutResolutions[fullPath] = resolution;
            }
        }
        else identity = await _executableGameIdentityFactory.CreateAsync(
            fullPath, operation.CancellationToken);
        EnsureConfigurationOperationCurrent(operation);
        var discovered = new DiscoveredGame(identity);
        if (!DiscoveredGames.Any(game => string.Equals(game.ExecutablePath, identity.ExecutablePath, StringComparison.OrdinalIgnoreCase))) DiscoveredGames.Add(discovered);
        SelectedDiscoveredGame = discovered;
        AutoSnapshotExecutablePath = identity.ExecutablePath ?? string.Empty;
        AutoSnapshotProcessName = identity.ProcessName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(NewGameName)) NewGameName = identity.Name;
        StatusText = _shortcutResolutionFailures.TryGetValue(fullPath, out string? failure)
            ? $"已保存快捷方式，但启动配置待验证：{failure}"
            : "已读取本地游戏启动入口；确认名称后创建游戏，再配置存档保护。";
    }

    public async Task<bool> TestPendingGameLaunchAsync()
    {
        ConfigurationOperationStamp operation = CaptureConfigurationOperationStamp();
        try
        {
            GameIdentity identity = operation.Identity;
            GameLaunchProfile launchProfile = BuildPendingLaunchProfile();
            string? validationError = ValidateLaunchProfile(launchProfile, requireResolvedShortcut: true);
            if (validationError is not null) throw new InvalidOperationException(validationError);
            GameLaunchResult result = await _gameLaunchService.LaunchAsync(
                launchProfile, identity.InstallDirectory, operation.CancellationToken);
            EnsureConfigurationOperationCurrent(operation);
            string[] detectedNames = result.DetectedProcesses
                .Where(process => process.IsStillRunning)
                .Select(process => GameProcessNameRules.Normalize(process.ProcessName))
                .Where(name => name.Length > 0 && !GameProcessNameRules.IsUnsafeGenericName(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            AddGameWizard.SetDetectedProcesses(detectedNames);
            AddGameWizard.LaunchValidated = result.LaunchRequestSucceeded
                && detectedNames.Length > 0;
            StatusText = result.Warning is null
                ? $"测试启动成功，已发现 {detectedNames.Length} 个可监控进程；请确认实际游戏进程。"
                : $"测试启动已完成：{result.Warning}";
            return AddGameWizard.LaunchValidated;
        }
        catch (Exception exception)
        {
            if (!IsConfigurationOperationCurrent(operation)) return false;
            AddGameWizard.LaunchValidated = false;
            ShowError("测试启动失败", exception);
            return false;
        }
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
        if (_isAddGameWizardActive)
            return SelectedDiscoveredGame?.Identity
                ?? new GameIdentity(NewGameName, GameIdentity.Custom, null, string.Empty, null, null);
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
        if (parameter is not CloudGame requestedGame || !IsAuthenticated) return;
        CloudGame? game = Games.FirstOrDefault(item =>
            string.Equals(item.GameId, requestedGame.GameId, StringComparison.Ordinal));
        if (game is null) return;
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

        Uri? operationServer = null;
        SessionStamp? session = null;
        bool queueAcquired = false;
        try
        {
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            if (IsGameRunningNow(profile))
                throw new InvalidOperationException("检测到游戏已经在运行。为避免在运行中恢复或重复启动，请先退出现有游戏进程。");
            _launchesInProgress[game.GameId] = session.Generation;
            RaiseCommandStates();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchDisabledReason)));
            SelectedGame = game;
            ApplyLocalProfileToSelectedGame(profile);
            AutoSnapshotExecutablePath = profile.ExecutablePath ?? launchProfile.Target;

            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            await _syncQueue.WaitAsync(session.CancellationToken);
            queueAcquired = true;
            EnsureSessionCurrent(session, server);
            if (IsGameProcessRunningNow(profile))
                throw new InvalidOperationException("检测到游戏已经在运行。为避免与同步或恢复任务交叉，请先退出现有游戏进程。");
            if (!await EnsureCloudFreshBeforeLaunchAsync(
                    game, profile, server, token, session)) return;
            EnsureSessionCurrent(session, server);

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
                EnsureSessionCurrent(session, server);
                _localGameProfiles[profile.GameId] = profile;
            }

            StatusText = $"已发送 {game.Name} 的启动请求，正在确认游戏进程…";
            GameLaunchResult launchResult = await _gameLaunchService.LaunchAsync(
                launchProfile,
                profile.InstallDirectory,
                session.CancellationToken);
            EnsureSessionCurrent(session, server);
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
                EnsureSessionCurrent(session, server);
                _localGameProfiles[profile.GameId] = profile;
            }

            StatusText = launchResult.Warning is null
                ? $"已确认 {game.Name} 的游戏进程正在运行。"
                : $"已发送 {game.Name} 的启动请求，但{launchResult.Warning}";
            RefreshGameRuntimeStatus();
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            ShowError("启动游戏失败", exception);
        }
        finally
        {
            if (queueAcquired) _syncQueue.Release();
            if (_launchesInProgress.TryGetValue(game.GameId, out long launchGeneration)
                && session is not null
                && launchGeneration == session.Generation)
                _launchesInProgress.Remove(game.GameId);
            RaiseCommandStates();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchDisabledReason)));
        }
    }

    /// <summary>游戏尚未启动时检查云端 HEAD；只有本机未改动才允许自动拉取，歧义场景必须由用户处理。</summary>
    private async Task<bool> EnsureCloudFreshBeforeLaunchAsync(
        CloudGame game,
        LocalGameProfile profile,
        Uri server,
        string token,
        SessionStamp session)
    {
        string userId = session.UserId;
        EnsureSessionCurrent(session, server);
        EnsureUiOperationTarget(game.GameId, userId, session.Generation);
        if (_activeConflicts.ContainsKey(game.GameId))
        {
            await ReloadSnapshotsAsync(server, token, game.GameId);
            EnsureUiOperationTarget(game.GameId, userId, session.Generation);
            CurrentPage = "时间线";
            StatusText = "当前游戏已有待处理的版本选择，已阻止启动。请先恢复当前云端 HEAD，或明确选择“保留本机版本并上传”。";
            RequestWindowsNotification(
                $"{game.Name} 启动已暂停",
                StatusText,
                WindowsNotificationKind.Warning);
            SyncConflictDetected?.Invoke(this, new SyncConflictEventArgs(game.GameId));
            return false;
        }
        await PrepareRegistrySnapshotsAsync(
            server, userId, game.GameId, profile.EffectiveRegistrySaveRules,
            session.CancellationToken);
        CloudFreshnessResult freshness = new(CloudFreshnessStatus.BaselineMissing, null, null);
        for (int attempt = 0; attempt < 3; attempt++)
        {
            freshness = await _cloudSyncService.CheckFreshnessAsync(
                server, token, userId, game.GameId, profile.EffectiveSaveRoots,
                session.CancellationToken);
            EnsureUiOperationTarget(game.GameId, userId, session.Generation);
            if (freshness.Status is CloudFreshnessStatus.UpToDate or CloudFreshnessStatus.LocalAhead)
                return true;

            if (freshness.Status == CloudFreshnessStatus.RemoteAheadLocalUnchanged
                && !string.IsNullOrWhiteSpace(freshness.RemoteHeadSnapshotId))
            {
                StatusText = "检测到云端有更新，正在启动游戏前安全拉取最新存档…";
                IReadOnlyList<RestoreResult> results = await _safeRestoreService.RestoreAsync(
                    server, token, userId, game.GameId, freshness.RemoteHeadSnapshotId,
                    profile.EffectiveSaveRoots, profile.EffectiveRegistrySaveRules,
                    session.CancellationToken,
                    () =>
                    {
                        EnsureSessionCurrent(session, server);
                        if (IsGameProcessRunningNow(profile))
                            throw new InvalidOperationException("游戏在云端存档下载期间已经启动，已在替换原存档前取消启动流程。");
                    });
                EnsureUiOperationTarget(game.GameId, userId, session.Generation);
                int safetyBackups = results.Count(result => result.SafetyBackupDirectory is not null);
                StatusText = safetyBackups > 0
                    ? $"已拉取云端存档，并保留 {safetyBackups} 份本机安全备份；正在再次确认云端 HEAD。"
                    : "已拉取云端存档；正在再次确认云端 HEAD。";
                continue;
            }
            break;
        }

        EnsureUiOperationTarget(game.GameId, userId, session.Generation);
        FileCount = freshness.LocalFileCount;
        LogicalSizeText = FormatBytes(freshness.LocalLogicalSize);
        _activeConflicts[game.GameId] = new SyncConflictContext(
            game.GameId,
            freshness.RemoteHeadSnapshotId,
            freshness.LocalFileCount,
            freshness.LocalLogicalSize);
        await ReloadSnapshotsAsync(server, token, game.GameId);
        EnsureUiOperationTarget(game.GameId, userId, session.Generation);
        CurrentPage = "时间线";
        StatusText = freshness.Status switch
        {
            CloudFreshnessStatus.Diverged => "云端和本机存档都发生了变化，已阻止启动；请先选择要保留的进度。",
            CloudFreshnessStatus.LocalDataMissing => "检测到本机存档大量缺失或被清空，已阻止启动；请恢复云端版本或明确确认本机状态。",
            CloudFreshnessStatus.RemoteAheadLocalUnchanged => "云端 HEAD 在连续拉取期间仍在变化，已暂停启动；请稍后重试或手动处理时间线。",
            _ => "本机没有可信的同步基线且已有存档，已阻止启动；请先恢复云端版本或明确保留本机版本。"
        };
        RequestWindowsNotification($"{game.Name} 启动已暂停", StatusText, WindowsNotificationKind.Warning);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveConflict)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveConflictRemoteHeadSnapshotId)));
        if (KeepLocalConflictCommand is AsyncCommand command) command.RaiseCanExecuteChanged();
        SyncConflictDetected?.Invoke(this, new SyncConflictEventArgs(game.GameId));
        return false;
    }

    private bool IsGameRunning(LocalGameProfile profile) =>
        GetMonitoredProcessNames(profile).Any(name => _runningProcessNames.Contains(GameProcessNameRules.Normalize(name)));

    private bool IsGameRunningNow(LocalGameProfile profile)
    {
        _runningProcessNames = SnapshotRunningProcessNames();
        RuntimeStatusVersion++;
        return IsGameRunning(profile);
    }

    private static bool IsGameProcessRunningNow(LocalGameProfile profile)
    {
        HashSet<string> running = SnapshotRunningProcessNames();
        return GetMonitoredProcessNames(profile)
            .Any(name => running.Contains(GameProcessNameRules.Normalize(name)));
    }

    // 运行状态刷新时先枚举一次全部进程并缓存进程名集合；读写均在 UI 线程，引用整体替换避免读到半更新集合。
    private HashSet<string> _runningProcessNames = new(StringComparer.OrdinalIgnoreCase);

    private void RefreshGameRuntimeStatus()
    {
        _runningProcessNames = SnapshotRunningProcessNames();
        RuntimeStatusVersion++;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(AutoSyncConfigurationText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchDisabledReason)));
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
        string? gameId = SelectedGame?.GameId;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            string token = await RequireDeviceTokenAsync(server);
            EnsureSessionCurrent(session, server);
            if (!_localGameProfiles.TryGetValue(gameId, out LocalGameProfile? savedProfile) || !IsAutomaticSyncProfileReady(savedProfile))
                throw new InvalidOperationException("启动入口或存档目录尚未配置完成，请先在游戏详情中完成配置");
            if (IsGameRunningNow(savedProfile))
                throw new InvalidOperationException("游戏正在运行，请退出游戏后再启用自动同步，以便先完成云端版本检查。");
            LocalGameProfile profile = savedProfile with { AutoSnapshotEnabled = true };
            await EnableAutomaticSyncAsync(
                server, token, gameId, profile, session.CancellationToken);
            EnsureSessionCurrent(session, server);
            bool enabled = _autoSyncCoordinator.ActiveGameIds.Contains(gameId);
            if (!enabled) throw new InvalidOperationException("自动同步监听未能启动，请检查启动入口和存档目录");
            await _localGameProfileStore.SaveAsync(profile, CancellationToken.None);
            EnsureSessionCurrent(session, server);
            _localGameProfiles[profile.GameId] = profile;
            RefreshGameRuntimeStatus();
            if (IsSelectedGame(gameId))
            {
                IsAutoSyncEnabled = true;
                StatusText = "自动同步已启用：每个已启用的游戏都会独立监听，游戏退出后排队增量同步。";
            }
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            if (gameId is null || IsSelectedGame(gameId)) ShowError("启用自动同步失败", exception);
        }
    }

    private async Task StopAutoSnapshotAsync()
    {
        string? gameId = SelectedGame?.GameId;
        if (gameId is null) return;
        Uri? operationServer = null;
        SessionStamp? session = null;
        try
        {
            Uri server = ParseServerUri();
            operationServer = server;
            session = CaptureSessionStamp(server);
            await _autoSyncCoordinator.DisableAsync(gameId);
            EnsureSessionCurrent(session, server);
            string serverKey = GameSaveServerIdentity.CreateStableKey(server);
            LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
                serverKey, session.UserId, gameId, session.CancellationToken);
            EnsureSessionCurrent(session, server);
            if (profile is not null)
            {
                LocalGameProfile disabledProfile = profile with { AutoSnapshotEnabled = false };
                await _localGameProfileStore.SaveAsync(disabledProfile, CancellationToken.None);
                EnsureSessionCurrent(session, server);
                _localGameProfiles[disabledProfile.GameId] = disabledProfile;
            }
            RefreshGameRuntimeStatus();
            if (IsSelectedGame(gameId))
            {
                IsAutoSyncEnabled = false;
                StatusText = "已停止当前游戏的自动同步；其他游戏的自动同步不受影响。";
            }
        }
        catch (Exception exception)
        {
            if (session is not null && operationServer is not null
                && !IsSessionCurrent(session, operationServer)) return;
            ShowError("停止自动同步失败", exception);
        }
    }

    private async Task<string> RequireDeviceTokenAsync(Uri server)
    {
        string? token = await _credentialStore.ReadAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("当前服务端没有设备 Token，请先注册或登录")
            : token;
    }

    private long BeginSessionTransition()
    {
        if (_learningBefore is not null || _learningCancellation is not null)
            ResetSaveLearningState(cancel: true);
        CancellationTokenSource[] activeSyncs;
        lock (_syncCancellations) activeSyncs = _syncCancellations.Values.ToArray();
        foreach (CancellationTokenSource activeSync in activeSyncs)
        {
            try { activeSync.Cancel(); }
            catch (ObjectDisposedException) { }
        }
        try { _sessionLifetime.Cancel(); }
        catch (AggregateException exception)
        {
            _appLogger.Error(
                "session.cancellation_callback_failed",
                exception,
                "取消旧账号会话时有回调失败");
        }
        _sessionLifetime.Dispose();
        _sessionLifetime = new CancellationTokenSource();
        return Interlocked.Increment(ref _sessionGeneration);
    }

    private async Task DrainSyncQueueAsync()
    {
        await _syncQueue.WaitAsync(CancellationToken.None);
        _syncQueue.Release();
    }

    private SessionStamp CaptureSessionStamp(Uri server)
    {
        string userId = RequireAuthenticatedUserId();
        return new SessionStamp(
            Volatile.Read(ref _sessionGeneration),
            GameSaveServerIdentity.CreateStableKey(server),
            userId,
            _sessionLifetime.Token);
    }

    private bool IsSessionCurrent(SessionStamp stamp, Uri server) =>
        stamp.Generation == Volatile.Read(ref _sessionGeneration)
        && !_sessionLifetime.IsCancellationRequested
        && IsAuthenticated
        && string.Equals(stamp.UserId, _authenticatedUserId, StringComparison.Ordinal)
        && string.Equals(stamp.ServerKey, GameSaveServerIdentity.CreateStableKey(server), StringComparison.Ordinal);

    private bool IsOperationSessionCurrent(string userId, long generation) =>
        generation == Volatile.Read(ref _sessionGeneration)
        && !_sessionLifetime.IsCancellationRequested
        && IsAuthenticated
        && string.Equals(userId, _authenticatedUserId, StringComparison.Ordinal);

    private void EnsureSessionCurrent(SessionStamp stamp, Uri server)
    {
        if (!IsSessionCurrent(stamp, server))
            throw new OperationCanceledException("账号会话已经变化，旧请求结果已丢弃。", stamp.CancellationToken);
    }

    private void ClearAuthenticatedUiState()
    {
        if (_learningBefore is not null || _learningCancellation is not null)
            ResetSaveLearningState(cancel: true);
        IsAutoSyncEnabled = false;
        IsAuthenticated = false;
        AuthenticatedUsername = string.Empty;
        _authenticatedUserId = string.Empty;
        _resumeAutomaticSyncAfterConfiguration = false;
        _isAddGameWizardActive = false;
        _addGameWizardReturnState = null;
        AddGameWizard.Reset();
        _gameSyncUiStates.Clear();
        _activeConflicts.Clear();
        _localGameProfiles.Clear();
        _launchesInProgress.Clear();
        Games.Clear();
        Snapshots.Clear();
        Devices.Clear();
        SelectedGame = null;
        SelectedSnapshot = null;
        SelectedDevice = null;
        SelectedDiscoveredGame = null;
        SelectedSaveLocationCandidate = null;
        SelectedAdditionalSaveRoot = null;
        SelectedRegistrySaveRule = null;
        GameSearchText = string.Empty;
        NewGameName = string.Empty;
        SaveDirectory = string.Empty;
        AdditionalSaveRootPath = string.Empty;
        RegistrySaveKeyPath = string.Empty;
        AutoSnapshotProcessName = string.Empty;
        AutoSnapshotExecutablePath = string.Empty;
        SaveLocationCandidates.Clear();
        AdditionalSaveRoots.Clear();
        RegistrySaveRules.Clear();
        SaveRootPreviews.Clear();
        RegistrySavePreviews.Clear();
        _previewedSaveDirectory = null;
        _previewedSaveDirectoryFingerprint = null;
        _shortcutResolutionFailures.Clear();
        _shortcutResolutions.Clear();
        IsSaveDirectoryConfirmed = false;
        FileCount = 0;
        LogicalSizeText = "0 B";
        SaveDirectoryPreviewText = "请选择当前游戏的存档目录并重新预览。";
        RestorePreviewText = "选择快照后可预览将恢复的文件数量与大小。";
        RetentionEnabled = false;
        RetentionCountText = "50";
        RetentionDaysText = "0";
        IsSyncing = false;
        SyncProgressText = "等待同步";
        SyncProgressValue = 0;
        SyncSummaryText = "暂无同步记录";
        QuotaUsageText = "尚未加载存储容量";
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasActiveConflict)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ActiveConflictRemoteHeadSnapshotId)));
    }

    private ConfigurationOperationStamp CaptureConfigurationOperationStamp() => new(
        Volatile.Read(ref _sessionGeneration),
        _sessionLifetime.Token,
        _isAddGameWizardActive,
        SelectedGame?.GameId,
        SelectedDiscoveredGame,
        GetCurrentGameIdentity());

    private bool IsConfigurationOperationCurrent(ConfigurationOperationStamp stamp) =>
        stamp.Generation == Volatile.Read(ref _sessionGeneration)
        && !stamp.CancellationToken.IsCancellationRequested
        && stamp.IsAddGameWizardActive == _isAddGameWizardActive
        && string.Equals(stamp.GameId, SelectedGame?.GameId, StringComparison.Ordinal)
        && Equals(stamp.DiscoveredGame, SelectedDiscoveredGame)
        && Equals(stamp.Identity, GetCurrentGameIdentity());

    private void EnsureConfigurationOperationCurrent(ConfigurationOperationStamp stamp)
    {
        if (!IsConfigurationOperationCurrent(stamp))
            throw new OperationCanceledException("账号、游戏或本机配置已经变化，旧操作结果已丢弃。", stamp.CancellationToken);
    }

    private bool CanStartAuthentication() =>
        !IsAuthenticated && Volatile.Read(ref _authenticationInProgress) == 0;

    private bool TryBeginAuthenticationOperation()
    {
        if (Interlocked.CompareExchange(ref _authenticationInProgress, 1, 0) != 0) return false;
        RaiseAuthenticationCommandStates();
        return true;
    }

    private void EndAuthenticationOperation()
    {
        Interlocked.Exchange(ref _authenticationInProgress, 0);
        RaiseAuthenticationCommandStates();
    }

    private void RaiseAuthenticationCommandStates()
    {
        if (RegisterCommand is AsyncCommand register) register.RaiseCanExecuteChanged();
        if (LoginCommand is AsyncCommand login) login.RaiseCanExecuteChanged();
    }

    private static void ValidateAccountIdentity(
        string userId,
        string deviceId,
        string username,
        string? expectedDeviceId)
    {
        if (string.IsNullOrWhiteSpace(userId) || userId.Length > 128
            || string.IsNullOrWhiteSpace(username) || username.Length > 128
            || !IsValidDeviceId(deviceId))
            throw new InvalidDataException("服务端返回的账号或设备身份不完整，已拒绝建立登录会话。");
        if (expectedDeviceId is not null
            && !string.Equals(deviceId, expectedDeviceId, StringComparison.Ordinal))
            throw new InvalidDataException("服务端返回的设备身份与本机不一致，已拒绝覆盖本机设备记录。");
    }

    private static bool IsValidDeviceId(string? value) =>
        value is { Length: >= 8 and <= 64 }
        && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '_' or '-');

    private string RequireAuthenticatedUserId() => string.IsNullOrWhiteSpace(_authenticatedUserId)
        ? throw new InvalidOperationException("当前账号身份尚未恢复，请重新登录")
        : _authenticatedUserId;

    private void RequestWindowsNotification(string title, string message, WindowsNotificationKind kind) =>
        WindowsNotificationRequested?.Invoke(this, new WindowsNotificationEventArgs(title, message, kind));

    private bool RequestConfirmation(string title, string message, string confirmText)
    {
        var request = new UserConfirmationEventArgs(title, message, confirmText);
        UserConfirmationRequested?.Invoke(this, request);
        return request.Confirmed;
    }

    private bool HasActiveConflictForSelectedGame() =>
        SelectedGame is not null && _activeConflicts.ContainsKey(SelectedGame.GameId) && !IsSelectedGameSyncing;

    private Uri ParseServerUri() => GameSaveServerIdentity.ParseAndValidate(ServerAddress);

    private void ShowError(string operation, Exception exception)
    {
        _appLogger.Error("operation.failed", exception, operation);
        ClientOperationError error = ClientOperationError.FromException(exception);
        if (error.Category == ErrorCategory.Authentication)
        {
            long failedGeneration = Volatile.Read(ref _sessionGeneration);
            _ = ValidateCurrentAuthenticationAfterFailureAsync(failedGeneration);
        }
        if (error.Category == ErrorCategory.Cancelled)
        {
            StatusText = "操作已取消。";
            return;
        }
        string requestId = string.IsNullOrWhiteSpace(error.RequestId) ? string.Empty : $"（请求 ID：{error.RequestId}）";
        string retry = error.CanRetry
            ? error.SuggestedRetryDelay is { } delay && delay > TimeSpan.Zero
                ? $" 可在约 {Math.Ceiling(delay.TotalSeconds):0} 秒后重试。"
                : " 可稍后重试。"
            : string.Empty;
        StatusText = $"{operation}：{error.UserMessage}{requestId}{retry}";
    }

    private static bool IsAmbiguousWriteFailure(Exception exception) => exception switch
    {
        GameSaveApiException api => api.StatusCode >= 500,
        HttpRequestException or TaskCanceledException or IOException or InvalidDataException => true,
        _ => false
    };

    public void HandleUnhandledCommandException(Exception exception) =>
        ShowError("操作失败", exception);

    public async Task<bool> PrepareForShutdownAsync()
    {
        _learningCancellation?.Cancel();
        _updateDownloadCancellation?.Cancel();
        CancellationTokenSource[] activeSyncs;
        lock (_syncCancellations) activeSyncs = _syncCancellations.Values.ToArray();
        foreach (CancellationTokenSource cancellation in activeSyncs) cancellation.Cancel();
        await _autoSyncCoordinator.DisableAllAsync();
        await _syncQueue.WaitAsync(CancellationToken.None);
        _syncQueue.Release();
        return true;
    }

    public async Task ResumeAfterCancelledShutdownAsync()
    {
        if (!IsAuthenticated) return;
        try
        {
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await RestoreAutomaticSyncProfilesAsync(server, token);
            if (SelectedGame is not null)
                IsAutoSyncEnabled = _autoSyncCoordinator.ActiveGameIds.Contains(SelectedGame.GameId);
        }
        catch (Exception exception) { ShowError("恢复自动同步失败", exception); }
    }

    private async Task ValidateCurrentAuthenticationAfterFailureAsync(long failedGeneration)
    {
        await _authenticationGate.WaitAsync();
        try
        {
            if (failedGeneration != Volatile.Read(ref _sessionGeneration)) return;
            Uri server;
            try { server = ParseServerUri(); }
            catch { return; }
            string? currentToken = await _credentialStore.ReadAsync(
                CredentialTargets.ForDeviceToken(server), CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(currentToken))
            {
                try
                {
                    CloudAccountSession session = await _apiClient.GetSessionAsync(
                        server, currentToken, CancellationToken.None);
                    if (failedGeneration == Volatile.Read(ref _sessionGeneration)
                        && string.Equals(session.UserId, _authenticatedUserId, StringComparison.Ordinal))
                        return;
                }
                catch (GameSaveApiException exception) when (exception.StatusCode is 401 or 403) { }
                catch (Exception exception)
                {
                    _appLogger.Error("authentication.recheck_failed", exception, "认证失败后复核当前会话失败");
                    return;
                }
            }

            long cleanupGeneration = BeginSessionTransition();
            await DrainSyncQueueAsync();
            try { await _autoSyncCoordinator.DisableAllAsync(); }
            catch (Exception exception)
            {
                _appLogger.Error(
                    "authentication.expired_monitor_cleanup_failed",
                    exception,
                    "会话过期后停止自动同步失败");
            }
            foreach (string target in new[]
                     {
                         CredentialTargets.ForDeviceToken(server),
                         CredentialTargets.ForAccountName(server),
                         CredentialTargets.ForAccountUserId(server)
                     })
            {
                try { await _credentialStore.DeleteAsync(target, CancellationToken.None); }
                catch (Exception exception)
                {
                    _appLogger.Error("authentication.cleanup.failed", exception, $"清理过期凭据 {target} 失败");
                }
            }
            if (cleanupGeneration == Volatile.Read(ref _sessionGeneration))
            {
                ClearAuthenticatedUiState();
                CurrentPage = "账户";
                StatusText = "登录状态已过期，请重新登录。";
            }
        }
        finally { _authenticationGate.Release(); }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return $"{value:0.##} {units[unit]}";
    }

    private static string GetClientVersionText()
    {
        Assembly assembly = typeof(MainViewModel).Assembly;
        string? informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+', 2)[0];
        return assembly.GetName().Version?.ToString(3) ?? "未知";
    }

    private static string GetClientReleaseChannelText()
    {
        return typeof(MainViewModel).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => string.Equals(
                attribute.Key,
                "ReleaseChannel",
                StringComparison.Ordinal))?
            .Value ?? "未知";
    }

    private bool CanCreateGame() =>
        IsAuthenticated && _isAddGameWizardActive && AddGameWizard.IsFinalConfigurationValid;

    private bool CanLaunchGame(object? parameter) =>
        IsAuthenticated
        && parameter is CloudGame game
        && Games.Any(item => string.Equals(item.GameId, game.GameId, StringComparison.Ordinal))
        && GetLaunchDisabledReason(game) is null;

    public string? GetLaunchDisabledReason(CloudGame game)
    {
        if (_launchesInProgress.ContainsKey(game.GameId)) return "正在验证该游戏的启动状态。";
        if (!_localGameProfiles.TryGetValue(game.GameId, out LocalGameProfile? profile)) return "尚未保存本机启动配置。";
        if (IsGameRunning(profile)) return "检测到游戏已经在运行，请先退出当前进程。";
        GameLaunchProfile? launchProfile = profile.EffectiveLaunchProfile;
        if (launchProfile is null) return "启动配置待验证。";
        if (launchProfile.TargetType == GameLaunchTargetType.StoreUri)
        {
            if (!Uri.TryCreate(launchProfile.Target, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme is not "steam" and not "com.epicgames.launcher"))
                return "平台启动地址无效。";
            return null;
        }
        if (!File.Exists(launchProfile.Target)) return "启动入口文件不存在。";
        if (!string.IsNullOrWhiteSpace(launchProfile.WorkingDirectory) && !Directory.Exists(launchProfile.WorkingDirectory))
            return "游戏工作目录不存在。";
        if (launchProfile.TargetType == GameLaunchTargetType.Shortcut
            && _shortcutResolutionFailures.TryGetValue(launchProfile.Target, out string? failure))
            return failure;
        return null;
    }

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
        return IsAuthenticated && SelectedGame is not null && !IsAutoSyncEnabled
            && _localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? profile)
            && IsAutomaticSyncProfileReady(profile)
            && !IsGameRunning(profile);
    }

    private static IReadOnlyList<SaveRootRule> GetAutomaticSyncFileRoots(LocalGameProfile profile) =>
        profile.EffectiveSaveRoots.Where(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static bool IsAutomaticSyncProfileReady(LocalGameProfile profile)
    {
        IReadOnlyList<SaveRootRule> fileRoots = GetAutomaticSyncFileRoots(profile);
        return profile.UserConfirmed
            && IsLaunchTargetValid(profile.EffectiveLaunchProfile)
            && GetMonitoredProcessNames(profile).Count > 0
            && (fileRoots.Count > 0 || profile.EffectiveRegistrySaveRules.Count > 0)
            && fileRoots.All(root => root.UserConfirmed && Directory.Exists(root.Path))
            && profile.EffectiveRegistrySaveRules.All(rule => rule.UserConfirmed);
    }
    private bool CanSynchronize() =>
        IsAuthenticated && SelectedGame is not null && !IsSelectedGameSyncing && HasConfirmedExistingFileRoots()
        && (!_localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? profile) || !IsGameRunning(profile));

    private bool CanRestore() =>
        CanSynchronize() && SelectedSnapshot is not null &&
        (SelectedGame is null || !_localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? profile) || !IsGameRunning(profile));

    private bool CanStartSaveLearning()
    {
        GameIdentity game = GetCurrentGameIdentity();
        return _learningCancellation is null && _learningBefore is null && IsLaunchTargetValid(CreateLaunchProfile(game));
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
            RegisterCommand, LoginCommand, CreateGameCommand, DeleteGameCommand, LogoutCommand,
            SyncCommand, RetrySyncCommand, CancelSyncCommand,
            ReloadGamesCommand,
            RestoreCommand, LaunchGameCommand, StartAutoSnapshotCommand, StopAutoSnapshotCommand,
            ConfirmSaveDirectoryCommand, PreviewSaveDirectoryCommand, StartSaveLearningCommand,
            CompleteSaveLearningCommand, CancelSaveLearningCommand
        })
        {
            if (command is AsyncCommand asyncCommand) asyncCommand.RaiseCanExecuteChanged();
            else if (command is DelegateCommand delegateCommand) delegateCommand.RaiseCanExecuteChanged();
        }
    }

    public void RefreshWizardCommandState()
    {
        if (CreateGameCommand is AsyncCommand command) command.RaiseCanExecuteChanged();
    }

    private sealed record AddGameWizardReturnState(
        CloudGame? SelectedGame,
        DiscoveredGame? SelectedDiscoveredGame,
        string NewGameName,
        string SaveDirectory,
        bool IsSaveDirectoryConfirmed,
        string ExecutablePath,
        string ProcessName,
        IReadOnlyList<SaveLocationCandidate> SaveLocationCandidates,
        SaveLocationCandidate? SelectedSaveLocationCandidate,
        IReadOnlyList<SaveRootRule> AdditionalSaveRoots,
        IReadOnlyList<RegistrySaveRule> RegistrySaveRules,
        IReadOnlyList<SaveRootPreview> SaveRootPreviews,
        IReadOnlyList<RegistrySavePreview> RegistrySavePreviews,
        string? PreviewedSaveDirectory,
        string? PreviewedSaveDirectoryFingerprint,
        string SaveDirectoryPreviewText,
        int FileCount,
        string LogicalSizeText);
    private sealed record SaveProfileConfirmationDraft(
        bool UserConfirmed,
        IReadOnlyList<SaveRootRule> AdditionalRoots,
        IReadOnlyList<RegistrySaveRule> RegistryRules);
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        RaiseCommandStates();
        return true;
    }

    private sealed class GameSyncUiState
    {
        public bool IsSyncing { get; set; }
        public string ProgressText { get; set; } = "等待立即备份";
        public double ProgressValue { get; set; }
        public string Summary { get; set; } = string.Empty;
        public string Error { get; set; } = string.Empty;
    }

    private sealed record SyncConflictContext(
        string GameId,
        string? RemoteHeadSnapshotId,
        int LocalFileCount,
        long LocalLogicalSize);
    private sealed record SessionStamp(
        long Generation,
        string ServerKey,
        string UserId,
        CancellationToken CancellationToken);

    private sealed record ConfigurationOperationStamp(
        long Generation,
        CancellationToken CancellationToken,
        bool IsAddGameWizardActive,
        string? GameId,
        DiscoveredGame? DiscoveredGame,
        GameIdentity Identity);
}

public sealed record SyncConflictEventArgs(string GameId);

public sealed class UserConfirmationEventArgs(string title, string message, string confirmText) : EventArgs
{
    public string Title { get; } = title;
    public string Message { get; } = message;
    public string ConfirmText { get; } = confirmText;
    public bool Confirmed { get; set; }
}

public sealed record WindowsNotificationEventArgs(
    string Title,
    string Message,
    WindowsNotificationKind Kind);

public enum WindowsNotificationKind
{
    Success,
    Warning,
    Error
}
