using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Reflection;
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
    private readonly Dictionary<string, string> _shortcutResolutionFailures = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ShortcutResolution> _shortcutResolutions = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _launchesInProgress = new(StringComparer.Ordinal);
    private readonly Dictionary<string, GameSyncUiState> _gameSyncUiStates = new(StringComparer.Ordinal);

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
    private CloudDevice? _selectedDevice;
    private ClientUpdatePreferences _updatePreferences = ClientUpdatePreferences.Default;
    private ClientUpdateRelease? _availableUpdate;
    private PreparedClientUpdate? _preparedUpdate;
    private CancellationTokenSource? _updateDownloadCancellation;
    private string _updateStatusText = "尚未检查客户端更新。";
    private bool _isUpdateBusy;
    private bool _isUpdateDownloading;
    private double _updateDownloadProgress;

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
        IUpdatePreferenceStore updatePreferenceStore)
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
        _autoStartEnabled = autoStartService.IsEnabled();
        _runtimeStatusTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(2) };
        _runtimeStatusTimer.Tick += (_, _) => RefreshGameRuntimeStatus();
        _runtimeStatusTimer.Start();

        RegisterCommand = new AsyncCommand(() => AuthenticateAsync(true));
        LoginCommand = new AsyncCommand(() => AuthenticateAsync(false));
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
        ConfirmSaveDirectoryCommand = new AsyncCommand(ConfirmSaveDirectoryAsync, IsCurrentSavePreviewValid);
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
        RevokeDeviceCommand = new AsyncCommand(
            RevokeDeviceAsync,
            parameter => parameter is CloudDevice device
                         && Devices.Any(item => string.Equals(item.DeviceId, device.DeviceId, StringComparison.Ordinal)));
        KeepLocalConflictCommand = new AsyncCommand(KeepLocalConflictAsync);
        NavigateCommand = new DelegateCommand(NavigateTo);
        SelectGameCommand = new AsyncCommand(SelectGameAsync);
        LaunchGameCommand = new AsyncCommand(LaunchGameAsync, CanLaunchGame);
        ToggleThemeCommand = new DelegateCommand(_ => ToggleTheme());
        ToggleAutoStartCommand = new AsyncCommand(ToggleAutoStartAsync);
        UpdateManifestCommand = new AsyncCommand(UpdateManifestAsync);
        CheckForUpdateCommand = new AsyncCommand(() => CheckForUpdatesAsync(true), () => !IsUpdateBusy);
        DownloadUpdateCommand = new AsyncCommand(DownloadUpdateAsync, () => CanDownloadUpdate);
        CancelUpdateDownloadCommand = new DelegateCommand(_ => _updateDownloadCancellation?.Cancel(), _ => IsUpdateDownloading);
        InstallUpdateCommand = new DelegateCommand(_ => UpdateInstallationRequested?.Invoke(this, EventArgs.Empty), _ => CanInstallUpdate);
        ToggleStartupUpdateCheckCommand = new AsyncCommand(ToggleStartupUpdateCheckAsync, () => !IsUpdateBusy);
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

    public event EventHandler? PasswordClearRequested;
    public event EventHandler? GameCreated;
    public event EventHandler? SyncConflictDetected;
    public event EventHandler? UpdateInstallationRequested;
    public event EventHandler<WindowsNotificationEventArgs>? WindowsNotificationRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>密码仅暂存于内存，并由 PasswordBox 调用此方法传入。</summary>
    public void SetPassword(string password) => _password = password;


    /// <summary>启动时尝试恢复已保存的设备会话；失效 Token 只清理凭据，不影响本机文件。</summary>
    public async Task InitializeAsync()
    {
        await LoadUpdatePreferencesAsync();
        _ = CheckForUpdatesOnStartupAsync();
        try
        {
            string? savedServerAddress = await _serverAddressStore.ReadAsync(CancellationToken.None);
            if (!string.IsNullOrWhiteSpace(savedServerAddress)) ServerAddress = savedServerAddress;
            await LoadManifestUpdateStatusAsync();
            Uri server = ParseServerUri();
            string? token = await _credentialStore.ReadAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
            if (string.IsNullOrWhiteSpace(token)) return;

            CloudAccountSession session = await _apiClient.GetSessionAsync(server, token, CancellationToken.None);
            _authenticatedUserId = session.UserId;
            AuthenticatedUsername = session.Username;
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountUserId(server), session.UserId, CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountName(server), session.Username, CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.StableDeviceId, session.DeviceId, CancellationToken.None);
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
            await _credentialStore.DeleteAsync(CredentialTargets.ForAccountUserId(server), CancellationToken.None);
            IsAuthenticated = false;
            AuthenticatedUsername = string.Empty;
            _authenticatedUserId = string.Empty;
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
        if (game is not CloudGame selected) return;
        SelectedGame = selected;
        string gameId = selected.GameId;
        try
        {
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await RestoreLocalProfileAsync(server, token);
            await ReloadSnapshotsAsync(server, token, gameId);
            if (IsSelectedGame(gameId))
                StatusText = $"已选择 {selected.Name}；可以查看最近快照或点击启动。";
        }
        catch (Exception exception)
        {
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
        try
        {
            Uri server = ParseServerUri();
            string deviceId = await _deviceIdentityProvider.GetOrCreateDeviceIdAsync(CancellationToken.None);
            AuthSession session = register
                ? await _apiClient.RegisterAsync(server, Username, _password, deviceId, Environment.MachineName, CancellationToken.None)
                : await _apiClient.LoginAsync(server, Username, _password, deviceId, Environment.MachineName, CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.ForDeviceToken(server), session.DeviceToken, CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountName(server), Username.Trim(), CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.ForAccountUserId(server), session.UserId, CancellationToken.None);
            await _credentialStore.SaveAsync(CredentialTargets.StableDeviceId, session.DeviceId, CancellationToken.None);
            await _serverAddressStore.SaveAsync(server.AbsoluteUri.TrimEnd('/'), CancellationToken.None);
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
        await _localGameProfileStore.ClaimLegacyAsync(
            GameSaveServerIdentity.CreateStableKey(server),
            RequireAuthenticatedUserId(),
            games.Select(game => game.GameId).ToArray(),
            CancellationToken.None);
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
        string? gameId = SelectedGame?.GameId;
        if (gameId is null) return;
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
            serverKey, RequireAuthenticatedUserId(), gameId, CancellationToken.None);
        if (profile is null || !IsSelectedGame(gameId)) return;

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
            await EnableAutomaticSyncAsync(server, token, gameId, profile);
        }
        if (IsSelectedGame(gameId))
            IsAutoSyncEnabled = _autoSyncCoordinator.ActiveGameIds.Contains(gameId);
    }


    /// <summary>加载所有已启用的本机配置，让每个游戏分别监听对应的进程和存档目录。</summary>
    private async Task RestoreAutomaticSyncProfilesAsync(Uri server, string token)
    {
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        IReadOnlyList<LocalGameProfile> profiles = await _localGameProfileStore.ListAsync(serverKey, RequireAuthenticatedUserId(), CancellationToken.None);
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
        IReadOnlyList<SaveRootRule> fileRoots = GetAutomaticSyncFileRoots(profile);
        if (!IsAutomaticSyncProfileReady(profile)) return;
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
                GameSaveServerIdentity.CreateStableKey(server), RequireAuthenticatedUserId(), gameId, cancellationToken);
            if (profile is null) throw new InvalidOperationException("未找到该游戏的本机同步配置。");
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, gameId, profile,
                SnapshotTrigger.GameExit, "游戏退出自动同步", false, cancellationToken);
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                ApplySyncResult(gameId, result);
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            _appLogger.Error("sync.automatic.failed", exception, $"游戏 {gameId} 的自动同步失败");
            await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
            {
                SetGameSyncError(gameId, $"自动同步失败：{exception.Message}");
                if (SelectedGame?.GameId == gameId) StatusText = $"自动同步失败：{exception.Message}";
                string gameName = Games.FirstOrDefault(game => game.GameId == gameId)?.Name ?? "游戏";
                RequestWindowsNotification($"{gameName} 自动备份失败", exception.Message, WindowsNotificationKind.Error);
            });
            throw;
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
        UpdateGameSyncState(gameId, state =>
        {
            state.IsSyncing = true;
            state.ProgressValue = 2;
            state.ProgressText = "正在等待同步任务…";
            state.Error = string.Empty;
        });
        try
        {
            await PrepareRegistrySnapshotsAsync(server, gameId, profile.EffectiveRegistrySaveRules);
            IProgress<CloudSyncProgress> progress = new Progress<CloudSyncProgress>(
                item => ReportSyncProgress(gameId, item));
            return await _cloudSyncService.SyncAsync(server, token, RequireAuthenticatedUserId(), gameId, roots, trigger, description,
                linked.Token, keepLocalOnConflict, progress);
        }
        finally
        {
            if (ReferenceEquals(_syncCancellation, linked)) _syncCancellation = null;
            IsSyncing = false;
            UpdateGameSyncState(gameId, state => state.IsSyncing = false);
            _syncQueue.Release();
        }
    }
    private void ReportSyncProgress(string gameId, CloudSyncProgress progress)
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
            UpdateGameSyncState(gameId, state =>
            {
                state.ProgressText = SyncProgressText;
                state.ProgressValue = SyncProgressValue;
            });
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
        double progressValue = result.Status == CloudSyncStatus.Success ? 100 : SyncProgressValue;
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
                SyncConflictDetected?.Invoke(this, EventArgs.Empty);
            }
        }
        RequestWindowsNotification(
            result.Status == CloudSyncStatus.Success ? $"{gameName} 备份完成" : $"{gameName} 需要处理同步冲突",
            statusText,
            result.Status == CloudSyncStatus.Success ? WindowsNotificationKind.Success : WindowsNotificationKind.Warning);
    }

    private GameSyncUiState? GetSelectedGameSyncState() =>
        SelectedGame is null || !_gameSyncUiStates.TryGetValue(SelectedGame.GameId, out GameSyncUiState? state)
            ? null
            : state;

    private void UpdateGameSyncState(string gameId, Action<GameSyncUiState> update)
    {
        if (!_gameSyncUiStates.TryGetValue(gameId, out GameSyncUiState? state))
        {
            state = new GameSyncUiState();
            _gameSyncUiStates[gameId] = state;
        }
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
    private string GetRegistryCacheDirectory(Uri server, string gameId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSaveManager", "registry",
        GameSaveServerIdentity.CreateStableKey(server), gameId);

    private void DeleteGeneratedGameData(Uri server, string gameId)
    {
        string gameDataRoot = Path.GetFullPath(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameSaveManager", "registry",
            GameSaveServerIdentity.CreateStableKey(server)));
        string gameDataDirectory = Path.GetFullPath(GetRegistryCacheDirectory(server, gameId));
        string relativePath = Path.GetRelativePath(gameDataRoot, gameDataDirectory);
        if (Path.IsPathRooted(relativePath) || relativePath.Equals("..", StringComparison.Ordinal)
            || relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            throw new IOException("游戏本地数据目录越过了应用数据边界，已停止删除");
        if (Directory.Exists(gameDataDirectory)) Directory.Delete(gameDataDirectory, true);
    }

    private async Task PrepareRegistrySnapshotsAsync(Uri server, string gameId, IReadOnlyList<RegistrySaveRule>? rules = null)
    {
        IReadOnlyList<RegistrySaveRule> effectiveRules = rules ?? RegistrySaveRules;
        if (effectiveRules.Count == 0) return;
        await _registrySaveSnapshotService.ExportAsync(GetRegistryCacheDirectory(server, gameId), effectiveRules, CancellationToken.None);
    }

    private IReadOnlyList<SaveRootRule> GetConfiguredSaveRoots(Uri server, string gameId) =>
        BuildConfiguredSaveRoots(server, gameId, IsSaveDirectoryConfirmed,
            AdditionalSaveRoots.ToArray(), RegistrySaveRules.ToArray());

    private IReadOnlyList<SaveRootRule> BuildConfiguredSaveRoots(
        Uri server,
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
            roots.Add(new SaveRootRule("registry", GetRegistryCacheDirectory(server, gameId),
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
        if (IsAuthenticated && SelectedGame is not null)
            await SaveLocalProfileAsync(ParseServerUri(), autoSnapshotEnabled: false);
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

    private async Task RemoveRegistrySaveRuleAsync()
    {
        string? gameId = SelectedGame?.GameId;
        if (SelectedRegistrySaveRule is null) throw new InvalidOperationException("请先选择要移除的注册表路径。");
        RegistrySaveRules.Remove(SelectedRegistrySaveRule);
        InvalidateSavePreview("注册表存档规则已变化，请重新预览并确认。");
        if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
        SelectedRegistrySaveRule = null;
        StatusText = "已移除注册表存档路径；保存或同步后会更新本机配置。";
    }
    private async Task RemoveAdditionalSaveRootAsync()
    {
        string? gameId = SelectedGame?.GameId;
        if (SelectedAdditionalSaveRoot is null) throw new InvalidOperationException("请先选择要移除的附加存档目录。");
        AdditionalSaveRoots.Remove(SelectedAdditionalSaveRoot);
        InvalidateSavePreview("附加存档目录已变化，请重新预览并确认全部规则。");
        if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
        SelectedAdditionalSaveRoot = null;
        StatusText = "已移除附加存档目录；保存或同步后会更新本机配置。";
        if (IsAuthenticated && SelectedGame is not null)
            await SaveLocalProfileAsync(ParseServerUri(), autoSnapshotEnabled: false);
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
        SaveProfileConfirmationDraft? confirmationDraft = null)
    {
        CloudGame selectedGame = SelectedGame ?? throw new InvalidOperationException("请先选择云端游戏");
        string gameId = selectedGame.GameId;
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
                configuredRoots.Add(new SaveRootRule("registry", GetRegistryCacheDirectory(server, gameId),
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
        string? validationError = ValidateLaunchProfile(launchProfile, requireResolvedShortcut);
        bool launchRequired = addGameWizardActive || autoSnapshotEnabled;
        if (validationError is not null && launchRequired) throw new InvalidOperationException(validationError);
        if (validationError is not null) launchProfile = null;
        await PrepareRegistrySnapshotsAsync(server, gameId, registryRules);
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
        _localGameProfiles[profile.GameId] = profile;
        RefreshGameRuntimeStatus();
        return profile;
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
    private async Task RefreshAutomaticSyncConfigurationAsync(Uri server, string token, LocalGameProfile profile)
    {
        await _autoSyncCoordinator.DisableAsync(profile.GameId);
        if (profile.AutoSnapshotEnabled) await EnableAutomaticSyncAsync(server, token, profile.GameId, profile);
    }

    private async Task<bool> SuspendAutomaticSyncForConfigurationAsync(string? expectedGameId = null)
    {
        string? gameId = SelectedGame?.GameId;
        if (expectedGameId is not null && !string.Equals(gameId, expectedGameId, StringComparison.Ordinal)) return false;
        if (gameId is null) return true;
        bool active = IsAutoSyncEnabled
            || _autoSyncCoordinator.ActiveGameIds.Contains(gameId)
            || (_localGameProfiles.TryGetValue(gameId, out LocalGameProfile? profile)
                && profile.AutoSnapshotEnabled);
        _resumeAutomaticSyncAfterConfiguration |= active;
        await _autoSyncCoordinator.DisableAsync(gameId);
        if (!IsSelectedGame(gameId)) return false;
        IsAutoSyncEnabled = false;
        return true;
    }
    private async Task ReloadRetentionAsync()
    {
        string? gameId = SelectedGame?.GameId;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await ReloadRetentionAsync(server, token, gameId);
            if (IsSelectedGame(gameId)) StatusText = "快照保留策略已加载。";
        }
        catch (Exception exception)
        {
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
        CloudRetentionPolicy policy = await _apiClient.GetRetentionPolicyAsync(
            server, token, gameId, CancellationToken.None);
        if (IsSelectedGame(gameId)) ApplyRetentionPolicy(policy);
    }
    private async Task SaveRetentionAsync()
    {
        string? gameId = SelectedGame?.GameId;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            if (!int.TryParse(RetentionCountText, out int count) || count is < 1 or > 500)
                throw new InvalidOperationException("保留数量必须是 1 到 500 之间的整数");
            if (!int.TryParse(RetentionDaysText, out int days) || days is < 0 or > 3650)
                throw new InvalidOperationException("保留天数必须是 0 到 3650 之间的整数，0 表示不按时间清理");
            bool enabled = RetentionEnabled;
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudRetentionPolicy policy = await _apiClient.UpdateRetentionPolicyAsync(
                server, token, gameId, enabled, count, days, CancellationToken.None);
            if (IsSelectedGame(gameId))
            {
                ApplyRetentionPolicy(policy);
                StatusText = policy.Enabled ? "快照自动保留策略已启用。" : "快照自动保留策略已关闭。";
            }
        }
        catch (Exception exception)
        {
            if (gameId is null || IsSelectedGame(gameId)) ShowError("保存保留策略失败", exception);
        }
    }

    private async Task CleanupRetentionAsync()
    {
        string? gameId = SelectedGame?.GameId;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudRetentionCleanupResult result = await _apiClient.CleanupRetentionAsync(
                server, token, gameId, CancellationToken.None);
            await ReloadSnapshotsAsync(server, token, gameId);
            await ReloadQuotaAsync(server, token);
            if (IsSelectedGame(gameId))
                StatusText = $"保留策略执行完成，删除 {result.DeletedSnapshotCount} 个历史快照。";
        }
        catch (Exception exception)
        {
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

    private async Task RevokeDeviceAsync(object? parameter)
    {
        try
        {
            if (parameter is not CloudDevice requestedDevice)
                throw new InvalidOperationException("撤销操作缺少明确的设备目标，已拒绝执行");
            string deviceId = requestedDevice.DeviceId;
            if (!Devices.Any(device => string.Equals(device.DeviceId, deviceId, StringComparison.Ordinal)))
                throw new InvalidOperationException("要撤销的设备已不在当前设备列表中，请刷新后重试");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await _apiClient.RevokeDeviceAsync(server, token, deviceId, CancellationToken.None);
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
        DiscoveredGame? discoveredGame = SelectedDiscoveredGame;
        try
        {
            if (!await SuspendAutomaticSyncForConfigurationAsync(gameId)) return;
            if (string.IsNullOrWhiteSpace(SaveDirectory) || !Directory.Exists(SaveDirectory)) throw new InvalidOperationException("请选择存在的存档目录。");
            string saveDirectory = SaveDirectory;
            IReadOnlyList<SaveRootRule> roots = BuildPreviewSaveRoots();
            IReadOnlyList<RegistrySaveRule> registryRules = RegistrySaveRules.ToArray();
            SaveProfilePreview preview = await _saveDirectoryPreviewService.PreviewProfileAsync(
                roots, registryRules, CancellationToken.None);
            IReadOnlyList<RegistrySavePreview> registryPreviews = await _registrySaveSnapshotService.PreviewAsync(
                registryRules, CancellationToken.None);
            if (!string.Equals(SelectedGame?.GameId, gameId, StringComparison.Ordinal)
                || !Equals(SelectedDiscoveredGame, discoveredGame)) return;
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
            InvalidateSavePreview("预览失败，请修正目录或扫描规则后重试。");
            ShowError("预览存档目录失败", exception);
        }
    }
    private async Task ConfirmSaveDirectoryAsync()
    {
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
                bool desiredAutoSync = _resumeAutomaticSyncAfterConfiguration || IsAutoSyncEnabled
                    || (_localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? existing)
                        && existing.AutoSnapshotEnabled);
                string token = desiredAutoSync ? await RequireDeviceTokenAsync(server) : string.Empty;
                LocalGameProfile savedProfile = await SaveLocalProfileAsync(server, desiredAutoSync, draft);
                await RefreshAutomaticSyncConfigurationAsync(server, token, savedProfile);
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
            IsAutoSyncEnabled = SelectedGame is not null
                && _autoSyncCoordinator.ActiveGameIds.Contains(SelectedGame.GameId);
            ShowError("确认存档目录失败", exception);
        }
    }

    private async Task StartSaveLearningAsync()
    {
        try
        {
            if (_learningBefore is not null) throw new InvalidOperationException("存档学习已经开始，请先完成或取消当前学习。");
            GameIdentity game = GetCurrentGameIdentity();
            if (string.IsNullOrWhiteSpace(game.ExecutablePath) || !File.Exists(game.ExecutablePath)) throw new InvalidOperationException("请先配置游戏 EXE。");
            _learningCancellation = new CancellationTokenSource();
            StatusText = "正在记录游戏运行前的文件元数据…";
            _learningBefore = await _runtimeSaveLearningService.CaptureBeforeAsync(game, _learningCancellation.Token);
            RaiseCommandStates();
            GameLaunchResult launchResult = await LaunchGameAsync(game, _learningCancellation.Token);
            StatusText = launchResult.Warning is null
                ? "已记录文件元数据并确认游戏正在运行；保存并退出后点击完成学习。"
                : $"已记录文件元数据并发送启动请求，但{launchResult.Warning}";
        }
        catch (OperationCanceledException)
        {
            ResetSaveLearningState(cancel: false);
            StatusText = "存档学习已取消。";
        }
        catch (Exception exception)
        {
            ResetSaveLearningState(cancel: true);
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
        RaiseCommandStates();
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
            ResetSaveLearningState(cancel: false);
            StatusText = $"学习完成：找到 {candidates.Count} 个候选目录，仍需用户确认。";
        }
        catch (OperationCanceledException)
        {
            ResetSaveLearningState(cancel: false);
            StatusText = "存档学习已取消。";
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
        catch (Exception exception) { ShowError("扫描本机游戏失败", exception); }
    }

    private async Task CreateGameAsync()
    {
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
            string token = await RequireDeviceTokenAsync(server);
            // LOCAL 仅用于客户端识别本地可执行文件，服务端创建云端游戏时应归入自定义游戏。
            CloudGame game = await _apiClient.CreateGameAsync(server, token, normalizedName, provider, providerGameId, CancellationToken.None);
            try
            {
            await ReloadGamesAsync(server, token);
            SelectedGame = Games.FirstOrDefault(item => item.GameId == game.GameId);
            SaveDirectory = pendingSaveDirectory;
            IsSaveDirectoryConfirmed = pendingDirectoryConfirmed;
            AutoSnapshotExecutablePath = pendingExecutablePath;
            AutoSnapshotProcessName = pendingProcessName;
            SelectedSaveLocationCandidate = pendingCandidate;
            AdditionalSaveRoots.Clear();
            foreach (SaveRootRule root in pendingAdditionalRoots) AdditionalSaveRoots.Add(root);
            RegistrySaveRules.Clear();
            foreach (RegistrySaveRule rule in pendingRegistryRules) RegistrySaveRules.Add(rule);
            if (SelectedGame is not null && pendingDirectoryConfirmed)
            {
                LocalGameProfile profile = await SaveLocalProfileAsync(server, enableAutomaticBackup);
                await RefreshAutomaticSyncConfigurationAsync(server, token, profile);
                IsAutoSyncEnabled = enableAutomaticBackup
                    && _autoSyncCoordinator.ActiveGameIds.Contains(game.GameId);
            }
            await ReloadSnapshotsAsync(server, token, game.GameId);
            await ReloadRetentionAsync(server, token, game.GameId);
            await ReloadQuotaAsync(server, token);
            NewGameName = string.Empty;
            CurrentPage = "游戏详情";
            StatusText = $"已创建云端游戏：{game.Name}。请继续配置启动入口和存档保护。";
            GameCreated?.Invoke(this, EventArgs.Empty);
            }
            catch
            {
                try
                {
                    await _apiClient.DeleteGameAsync(
                        server, token, game.GameId, CancellationToken.None);
                    await _localGameProfileStore.DeleteAsync(
                        GameSaveServerIdentity.CreateStableKey(server),
                        RequireAuthenticatedUserId(),
                        game.GameId,
                        CancellationToken.None);
                    await _cloudSyncService.DeleteLocalStateAsync(
                        server, RequireAuthenticatedUserId(), game.GameId, CancellationToken.None);
                }
                catch (Exception compensationFailure)
                {
                    _appLogger.Error(
                        "game.create.compensation.failed",
                        compensationFailure,
                        $"云端存在未完成游戏：{game.GameId}");
                }
                throw;
            }
        }
        catch (Exception exception) { ShowError("创建游戏失败", exception); }
    }

    /// <summary>删除云端游戏及全部云端快照，并移除这台电脑的对应同步配置。</summary>
    private async Task DeleteGameAsync(object? parameter)
    {
        try
        {
            if (parameter is not CloudGame requestedGame)
                throw new InvalidOperationException("删除操作缺少明确的游戏目标，已拒绝执行");
            CloudGame targetGame = Games.FirstOrDefault(game =>
                    string.Equals(game.GameId, requestedGame.GameId, StringComparison.Ordinal))
                ?? throw new InvalidOperationException("要删除的游戏已不在当前游戏库中，请刷新后重试");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string gameId = targetGame.GameId;
            string gameName = targetGame.Name;
            await _autoSyncCoordinator.DisableAsync(gameId);
            await _apiClient.DeleteGameAsync(server, token, gameId, CancellationToken.None);
            string serverKey = GameSaveServerIdentity.CreateStableKey(server);
            var cleanupWarnings = new List<string>();
            try
            {
                await _localGameProfileStore.DeleteAsync(serverKey, RequireAuthenticatedUserId(), gameId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                cleanupWarnings.Add("本机游戏配置稍后需要重新清理");
                _appLogger.Error("game.delete.local_profile_cleanup_failed", exception, $"清理游戏 {gameId} 的本机配置失败");
            }
            try
            {
                await _cloudSyncService.DeleteLocalStateAsync(server, RequireAuthenticatedUserId(), gameId, CancellationToken.None);
            }
            catch (Exception exception)
            {
                cleanupWarnings.Add("本机同步状态稍后需要重新清理");
                _appLogger.Error("game.delete.sync_state_cleanup_failed", exception, $"清理游戏 {gameId} 的本机同步状态失败");
            }
            _localGameProfiles.Remove(gameId);
            _gameSyncUiStates.Remove(gameId);
            try { DeleteGeneratedGameData(server, gameId); }
            catch (Exception exception)
            {
                cleanupWarnings.Add("注册表临时缓存未能清理");
                _appLogger.Error("game.delete.generated_cache_cleanup_failed", exception, $"清理游戏 {gameId} 的生成缓存失败");
            }
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
            try { await ReloadQuotaAsync(server, token); }
            catch (Exception exception)
            {
                cleanupWarnings.Add("存储容量未能刷新");
                _appLogger.Error("game.delete.quota_refresh_failed", exception, "删除游戏后刷新容量失败");
            }
            string warning = cleanupWarnings.Count == 0 ? string.Empty : $" 注意：{string.Join("；", cleanupWarnings)}。";
            StatusText = $"已删除游戏“{gameName}”、全部云端存档及这台电脑上的对应设置；本机原始存档未被删除。{warning}";
        }
        catch (Exception exception) { ShowError("删除游戏失败", exception); }
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
        try
        {
            Uri server = ParseServerUri();
            await _autoSyncCoordinator.DisableAllAsync();
            await _credentialStore.DeleteAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
            await _credentialStore.DeleteAsync(CredentialTargets.ForAccountName(server), CancellationToken.None);
            await _credentialStore.DeleteAsync(CredentialTargets.ForAccountUserId(server), CancellationToken.None);
            IsAutoSyncEnabled = false;
            IsAuthenticated = false;
            AuthenticatedUsername = string.Empty;
            _authenticatedUserId = string.Empty;
            _gameSyncUiStates.Clear();
            _localGameProfiles.Clear();
            Games.Clear(); Snapshots.Clear(); Devices.Clear();
            SelectedGame = null; SelectedSnapshot = null; SelectedDevice = null;
            SaveDirectory = string.Empty;
            AdditionalSaveRoots.Clear();
            RegistrySaveRules.Clear();
            QuotaUsageText = "尚未加载存储容量";
            NavigateTo("账户");
            StatusText = "已退出登录；本机游戏存档文件不会被删除。";
        }
        catch (Exception exception) { ShowError("退出登录失败", exception); }
    }
    private async Task BuildManifestAsync()
    {
        string? gameId = SelectedGame?.GameId;
        try
        {
            StatusText = "正在完整扫描目录并计算 SHA-256…";
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            IReadOnlyList<SaveRootRule> saveRoots = GetConfiguredSaveRoots(server, gameId);
            IReadOnlyList<RegistrySaveRule> registryRules = RegistrySaveRules.ToArray();
            await PrepareRegistrySnapshotsAsync(server, gameId, registryRules);
            IReadOnlyList<SnapshotFile> files = await _manifestBuilder.BuildAsync(saveRoots, CancellationToken.None);
            if (!IsSelectedGame(gameId)) return;
            FileCount = files.Count;
            LogicalSizeText = FormatBytes(files.Sum(file => file.Size));
            StatusText = "Manifest 已构建，Hash 缓存已写入本地 SQLite。";
        }
        catch (Exception exception)
        {
            if (gameId is null || IsSelectedGame(gameId)) ShowError("扫描失败", exception);
        }
    }

    private async Task SyncAsync()
    {
        string? gameId = SelectedGame?.GameId;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await SaveLocalProfileAsync(server, IsAutoSyncEnabled);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, gameId, GetRequiredLocalProfile(gameId),
                SnapshotTrigger.Manual, "手动同步", false, CancellationToken.None);
            ApplySyncResult(gameId, result);
            await ReloadSnapshotsAsync(server, token, gameId);
            await ReloadQuotaAsync(server, token);
        }
        catch (OperationCanceledException)
        {
            if (gameId is not null) SetGameSyncError(gameId, "同步已取消；下次同步会安全复用已上传内容。");
            StatusText = "同步已取消；下次同步会安全复用已上传内容。";
            RequestWindowsNotification("存档备份已取消", StatusText, WindowsNotificationKind.Warning);
        }
        catch (Exception exception)
        {
            if (gameId is not null) SetGameSyncError(gameId, $"同步失败：{ClientOperationError.FromException(exception).UserMessage}");
            RequestWindowsNotification("存档备份失败", ClientOperationError.FromException(exception).UserMessage, WindowsNotificationKind.Error);
            ShowError("同步失败", exception);
        }
    }

    private async Task KeepLocalConflictAsync()
    {
        string? gameId = SelectedGame?.GameId;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await SaveLocalProfileAsync(server, IsAutoSyncEnabled);
            CloudSyncResult result = await RunQueuedSyncAsync(server, token, gameId, GetRequiredLocalProfile(gameId),
                SnapshotTrigger.Manual, "多设备冲突：保留本机版本", true, CancellationToken.None);
            ApplySyncResult(gameId, result);
            await ReloadSnapshotsAsync(server, token, gameId);
            await ReloadQuotaAsync(server, token);
        }
        catch (OperationCanceledException)
        {
            if (gameId is not null) SetGameSyncError(gameId, "同步已取消。");
            StatusText = "同步已取消。";
        }
        catch (Exception exception)
        {
            if (gameId is not null) SetGameSyncError(gameId, $"保留本机版本失败：{ClientOperationError.FromException(exception).UserMessage}");
            ShowError("保留本机版本失败", exception);
        }
    }

    private async Task ReloadSnapshotsFromUiAsync()
    {
        string? gameId = SelectedGame?.GameId;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            await ReloadSnapshotsAsync(server, await RequireDeviceTokenAsync(server), gameId);
            if (IsSelectedGame(gameId)) StatusText = $"已加载 {Snapshots.Count} 个快照版本。";
        }
        catch (Exception exception)
        {
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
        IReadOnlyList<CloudSnapshotSummary> snapshots = await _apiClient.ListSnapshotsAsync(
            server, token, gameId, 100, CancellationToken.None);
        if (SelectedGame?.GameId != gameId) return;
        Snapshots.Clear();
        foreach (CloudSnapshotSummary snapshot in snapshots) Snapshots.Add(snapshot);
        SelectedSnapshot = Snapshots.FirstOrDefault();
        ApplyCloudPathSuggestions();
    }

    /// <summary>仅把云端历史路径当作待确认参考，不会绕过目录预览或自动写入本机配置。</summary>
    private void ApplyCloudPathSuggestions()
    {
        if (SelectedGame is null || _localGameProfiles.ContainsKey(SelectedGame.GameId)) return;
        CloudSnapshotRoot[] fileRoots = Snapshots.FirstOrDefault()?.Roots?
            .Where(root => string.Equals(root.RootType, "FILE", StringComparison.OrdinalIgnoreCase)
                           && !string.IsNullOrWhiteSpace(root.PathTemplate))
            .ToArray() ?? [];
        if (fileRoots.Length == 0) return;

        var resolved = fileRoots
            .Select(root => (Root: root, Path: _savePathTemplateService.Resolve(root.PathTemplate!)))
            .Where(item => !string.IsNullOrWhiteSpace(item.Path) && Directory.Exists(item.Path))
            .ToArray();
        if (resolved.Length == 0) return;

        SaveLocationCandidates.Clear();
        foreach (var item in resolved)
        {
            SaveLocationCandidates.Add(new SaveLocationCandidate(
                item.Path!, Math.Clamp(item.Root.Confidence, 0, 100), SaveLocationSource.CloudHistory,
                $"来自最近一次云端备份的路径记录：{item.Root.PathTemplate}", 0, 0, null, [], true));
        }
        SaveDirectory = resolved[0].Path!;
        SelectedSaveLocationCandidate = SaveLocationCandidates[0];
        IsSaveDirectoryConfirmed = false;
        AdditionalSaveRoots.Clear();
        foreach (var item in resolved.Skip(1))
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
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            if (snapshotId is null) throw new InvalidOperationException("请从时间线选择要恢复的快照");
            if (string.IsNullOrWhiteSpace(SaveDirectory)) throw new InvalidOperationException("请先填写本地存档目录");
            Uri server = ParseServerUri();
            string userId = RequireAuthenticatedUserId();
            IReadOnlyList<SaveRootRule> saveRoots = GetConfiguredSaveRoots(server, gameId);
            IReadOnlyList<RegistrySaveRule> registryRules = RegistrySaveRules.ToArray();
            string token = await RequireDeviceTokenAsync(server);
            if (IsSelectedGame(gameId)) StatusText = "正在等待同步任务空闲后开始恢复…";
            await _syncQueue.WaitAsync(CancellationToken.None);
            IReadOnlyList<RestoreResult> results;
            try
            {
                if (IsSelectedGame(gameId)) StatusText = "正在下载、校验并安全恢复快照…";
                results = await _safeRestoreService.RestoreAsync(
                    server, token, userId, gameId, snapshotId, saveRoots, registryRules, CancellationToken.None);
            }
            finally
            {
                _syncQueue.Release();
            }
            int backups = results.Count(item => item.SafetyBackupDirectory is not null);
            if (IsSelectedGame(gameId))
                StatusText = backups == 0
                    ? $"已恢复快照 {snapshotId} 到 {results.Count} 个存档目录。"
                    : $"已恢复快照 {snapshotId} 到 {results.Count} 个存档目录；已保留 {backups} 份原存档安全备份。";
        }
        catch (Exception exception)
        {
            if (gameId is null || IsSelectedGame(gameId)) ShowError("恢复存档失败", exception);
        }
    }


    private async Task LoadRestorePreviewAsync()
    {
        string? gameId = SelectedGame?.GameId;
        string? snapshotId = SelectedSnapshot?.SnapshotId;
        try
        {
            if (gameId is null || snapshotId is null)
                throw new InvalidOperationException("请先选择游戏和要预览的快照");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudSnapshotManifest manifest = await _apiClient.GetSnapshotAsync(
                server, token, gameId, snapshotId, CancellationToken.None);
            if (!IsSelectedGame(gameId) || !string.Equals(SelectedSnapshot?.SnapshotId, snapshotId, StringComparison.Ordinal)) return;
            long totalSize = manifest.Files.Sum(file => file.Size);
            string examples = string.Join("、", manifest.Files.Take(3).Select(file => file.RelativePath));
            RestorePreviewText = $"将恢复 {manifest.Files.Count} 个文件，共 {FormatBytes(totalSize)}。" +
                (string.IsNullOrWhiteSpace(examples) ? string.Empty : $" 示例：{examples}");
            StatusText = "恢复预览已加载；真正恢复前仍会创建安全备份并逐文件校验。";
        }
        catch (Exception exception)
        {
            if (gameId is null || IsSelectedGame(gameId)) ShowError("加载恢复预览失败", exception);
        }
    }

    private async Task ExportSnapshotAsync()
    {
        CloudGame? game = SelectedGame;
        CloudSnapshotSummary? snapshot = SelectedSnapshot;
        try
        {
            if (game is null || snapshot is null)
                throw new InvalidOperationException("请先选择游戏和要导出的快照");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
            string name = string.Concat(game.Name.Select(character => Path.GetInvalidFileNameChars().Contains(character) ? '_' : character));
            string destination = Path.Combine(downloads, $"{name}-{snapshot.LocalCreateTime:yyyyMMdd-HHmmss}.zip");
            if (IsSelectedGame(game.GameId)) StatusText = "正在下载并校验快照内容，然后导出 ZIP…";
            string exported = await _snapshotExportService.ExportAsync(
                server, token, game.GameId, snapshot.SnapshotId, destination, CancellationToken.None);
            if (IsSelectedGame(game.GameId)) StatusText = $"快照已导出到：{exported}";
        }
        catch (Exception exception)
        {
            if (game is null || IsSelectedGame(game.GameId)) ShowError("导出快照失败", exception);
        }
    }
    /// <summary>删除已明确确认的历史快照；服务端会拒绝删除当前同步 HEAD。</summary>
    private async Task DeleteSnapshotAsync()
    {
        string? gameId = SelectedGame?.GameId;
        string? snapshotId = SelectedSnapshot?.SnapshotId;
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            if (snapshotId is null) throw new InvalidOperationException("请从时间线选择要删除的历史快照");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            await _apiClient.DeleteSnapshotAsync(
                server, token, gameId, snapshotId, CancellationToken.None);
            await ReloadSnapshotsAsync(server, token, gameId);
            await ReloadQuotaAsync(server, token);
            if (IsSelectedGame(gameId))
                StatusText = $"已删除历史快照 {snapshotId}；未被其他快照引用的内容将按云端清理策略回收。";
        }
        catch (Exception exception)
        {
            if (gameId is null || IsSelectedGame(gameId)) ShowError("删除历史快照失败", exception);
        }
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
        GameIdentity identity;
        if (string.Equals(Path.GetExtension(fullPath), ".lnk", StringComparison.OrdinalIgnoreCase))
        {
            ShortcutResolution resolution = await _shortcutResolver.ResolveAsync(fullPath, CancellationToken.None);
            if (resolution.Resolved && resolution.TargetPath is { Length: > 0 } target)
            {
                GameIdentity resolvedIdentity = await _executableGameIdentityFactory.CreateAsync(target, CancellationToken.None);
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
        else identity = await _executableGameIdentityFactory.CreateAsync(fullPath, CancellationToken.None);
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
        try
        {
            GameIdentity identity = GetCurrentGameIdentity();
            GameLaunchProfile launchProfile = BuildPendingLaunchProfile();
            string? validationError = ValidateLaunchProfile(launchProfile, requireResolvedShortcut: true);
            if (validationError is not null) throw new InvalidOperationException(validationError);
            GameLaunchResult result = await _gameLaunchService.LaunchAsync(
                launchProfile, identity.InstallDirectory, CancellationToken.None);
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
            _launchesInProgress.Add(game.GameId);
            RaiseCommandStates();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchDisabledReason)));
            SelectedGame = game;
            SaveDirectory = profile.SaveDirectory;
            IsSaveDirectoryConfirmed = profile.UserConfirmed;
            AutoSnapshotExecutablePath = profile.ExecutablePath ?? launchProfile.Target;

            if (!await EnsureCloudFreshBeforeLaunchAsync(game, profile)) return;

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
        finally
        {
            _launchesInProgress.Remove(game.GameId);
            RaiseCommandStates();
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchDisabledReason)));
        }
    }

    /// <summary>游戏尚未启动时检查云端 HEAD；只有本机未改动才允许自动拉取，歧义场景必须由用户处理。</summary>
    private async Task<bool> EnsureCloudFreshBeforeLaunchAsync(CloudGame game, LocalGameProfile profile)
    {
        Uri server = ParseServerUri();
        string token = await RequireDeviceTokenAsync(server);
        await PrepareRegistrySnapshotsAsync(server, game.GameId, profile.EffectiveRegistrySaveRules);
        CloudFreshnessResult freshness = await _cloudSyncService.CheckFreshnessAsync(
            server, token, RequireAuthenticatedUserId(), game.GameId, profile.EffectiveSaveRoots, CancellationToken.None);
        if (freshness.Status == CloudFreshnessStatus.UpToDate) return true;

        if (freshness.Status == CloudFreshnessStatus.RemoteAheadLocalUnchanged
            && !string.IsNullOrWhiteSpace(freshness.RemoteHeadSnapshotId))
        {
            StatusText = "检测到云端有更新，正在启动游戏前安全拉取最新存档…";
            await _syncQueue.WaitAsync(CancellationToken.None);
            try
            {
                IReadOnlyList<RestoreResult> results = await _safeRestoreService.RestoreAsync(
                    server, token, RequireAuthenticatedUserId(), game.GameId, freshness.RemoteHeadSnapshotId,
                    profile.EffectiveSaveRoots, profile.EffectiveRegistrySaveRules, CancellationToken.None);
                int safetyBackups = results.Count(result => result.SafetyBackupDirectory is not null);
                StatusText = safetyBackups > 0
                    ? $"已拉取云端最新存档，并保留 {safetyBackups} 份本机安全备份；正在启动游戏。"
                    : "已拉取云端最新存档；正在启动游戏。";
                RequestWindowsNotification($"{game.Name} 云端存档已更新", StatusText, WindowsNotificationKind.Success);
                return true;
            }
            finally
            {
                _syncQueue.Release();
            }
        }

        await ReloadSnapshotsAsync(server, token, game.GameId);
        CurrentPage = "时间线";
        StatusText = freshness.Status == CloudFreshnessStatus.Diverged
            ? "云端和本机存档都发生了变化，已阻止启动；请先选择要保留的进度。"
            : "本机没有可信的同步基线且已有存档，已阻止启动；请先恢复云端版本或明确保留本机版本。";
        RequestWindowsNotification($"{game.Name} 启动已暂停", StatusText, WindowsNotificationKind.Warning);
        SyncConflictDetected?.Invoke(this, EventArgs.Empty);
        return false;
    }

    private bool IsGameRunning(LocalGameProfile profile) =>
        GetMonitoredProcessNames(profile).Any(name => _runningProcessNames.Contains(GameProcessNameRules.Normalize(name)));

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
        try
        {
            if (gameId is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            if (!_localGameProfiles.TryGetValue(gameId, out LocalGameProfile? savedProfile) || !IsAutomaticSyncProfileReady(savedProfile))
                throw new InvalidOperationException("启动入口或存档目录尚未配置完成，请先在游戏详情中完成配置");
            LocalGameProfile profile = savedProfile with { AutoSnapshotEnabled = true };
            await EnableAutomaticSyncAsync(server, token, gameId, profile);
            bool enabled = _autoSyncCoordinator.ActiveGameIds.Contains(gameId);
            if (!enabled) throw new InvalidOperationException("自动同步监听未能启动，请检查启动入口和存档目录");
            await _localGameProfileStore.SaveAsync(profile, CancellationToken.None);
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
            if (gameId is null || IsSelectedGame(gameId)) ShowError("启用自动同步失败", exception);
        }
    }

    private async Task StopAutoSnapshotAsync()
    {
        string? gameId = SelectedGame?.GameId;
        if (gameId is null) return;
        Uri server = ParseServerUri();
        await _autoSyncCoordinator.DisableAsync(gameId);
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
            serverKey, RequireAuthenticatedUserId(), gameId, CancellationToken.None);
        if (profile is not null)
        {
            LocalGameProfile disabledProfile = profile with { AutoSnapshotEnabled = false };
            await _localGameProfileStore.SaveAsync(disabledProfile, CancellationToken.None);
            _localGameProfiles[disabledProfile.GameId] = disabledProfile;
        }
        RefreshGameRuntimeStatus();
        if (IsSelectedGame(gameId))
        {
            IsAutoSyncEnabled = false;
            StatusText = "已停止当前游戏的自动同步；其他游戏的自动同步不受影响。";
        }
    }

    private async Task<string> RequireDeviceTokenAsync(Uri server)
    {
        string? token = await _credentialStore.ReadAsync(CredentialTargets.ForDeviceToken(server), CancellationToken.None);
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("当前服务端没有设备 Token，请先注册或登录")
            : token;
    }

    private string RequireAuthenticatedUserId() => string.IsNullOrWhiteSpace(_authenticatedUserId)
        ? throw new InvalidOperationException("当前账号身份尚未恢复，请重新登录")
        : _authenticatedUserId;

    private void RequestWindowsNotification(string title, string message, WindowsNotificationKind kind) =>
        WindowsNotificationRequested?.Invoke(this, new WindowsNotificationEventArgs(title, message, kind));

    private Uri ParseServerUri() => GameSaveServerIdentity.ParseAndValidate(ServerAddress);

    private void ShowError(string operation, Exception exception)
    {
        _appLogger.Error("operation.failed", exception, operation);
        ClientOperationError error = ClientOperationError.FromException(exception);
        if (error.Category == ErrorCategory.Authentication)
        {
            IsAuthenticated = false;
            AuthenticatedUsername = string.Empty;
            _authenticatedUserId = string.Empty;
            _gameSyncUiStates.Clear();
            _localGameProfiles.Clear();
            Games.Clear();
            SelectedGame = null;
            CurrentPage = "账户";
            _ = ClearExpiredAuthenticationAsync();
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

    private async Task ClearExpiredAuthenticationAsync()
    {
        try
        {
            Uri server = ParseServerUri();
            await _autoSyncCoordinator.DisableAllAsync();
            await _credentialStore.DeleteAsync(
                CredentialTargets.ForDeviceToken(server), CancellationToken.None);
            await _credentialStore.DeleteAsync(
                CredentialTargets.ForAccountName(server), CancellationToken.None);
            await _credentialStore.DeleteAsync(
                CredentialTargets.ForAccountUserId(server), CancellationToken.None);
        }
        catch (Exception exception)
        {
            _appLogger.Error("authentication.cleanup.failed", exception, "清理过期设备凭据失败");
        }
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
        parameter is CloudGame game && GetLaunchDisabledReason(game) is null;

    public string? GetLaunchDisabledReason(CloudGame game)
    {
        if (_launchesInProgress.Contains(game.GameId)) return "正在验证该游戏的启动状态。";
        if (!_localGameProfiles.TryGetValue(game.GameId, out LocalGameProfile? profile)) return "尚未保存本机启动配置。";
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
            && IsAutomaticSyncProfileReady(profile);
    }

    private static IReadOnlyList<SaveRootRule> GetAutomaticSyncFileRoots(LocalGameProfile profile) =>
        profile.EffectiveSaveRoots.Where(root => !string.Equals(root.RootId, "registry", StringComparison.OrdinalIgnoreCase)).ToArray();

    private static bool IsAutomaticSyncProfileReady(LocalGameProfile profile)
    {
        IReadOnlyList<SaveRootRule> fileRoots = GetAutomaticSyncFileRoots(profile);
        return profile.UserConfirmed
            && IsLaunchTargetValid(profile.EffectiveLaunchProfile)
            && GetMonitoredProcessNames(profile).Count > 0
            && fileRoots.Count > 0
            && fileRoots.All(root => root.UserConfirmed && Directory.Exists(root.Path));
    }
    private bool CanSynchronize() =>
        IsAuthenticated && !IsSyncing && SelectedGame is not null && HasConfirmedExistingFileRoots();

    private bool CanRestore() =>
        CanSynchronize() && SelectedSnapshot is not null &&
        (SelectedGame is null || !_localGameProfiles.TryGetValue(SelectedGame.GameId, out LocalGameProfile? profile) || !IsGameRunning(profile));

    private bool CanStartSaveLearning()
    {
        GameIdentity game = GetCurrentGameIdentity();
        return _learningBefore is null && IsLaunchTargetValid(CreateLaunchProfile(game));
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
