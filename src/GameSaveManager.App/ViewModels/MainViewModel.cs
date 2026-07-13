using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Windows.Data;
using GameSaveManager.App.Common;
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
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.App.ViewModels;

/// <summary>主窗口交互编排；业务 I/O 均通过 Application 服务执行。</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SaveManifestBuilder _manifestBuilder;
    private readonly IGameSaveApiClient _apiClient;
    private readonly CloudSyncService _cloudSyncService;
    private readonly SafeRestoreService _safeRestoreService;
    private readonly IAutoSnapshotMonitor _autoSnapshotMonitor;
    private readonly IGameDiscoveryService _gameDiscoveryService;
    private readonly ILocalGameProfileStore _localGameProfileStore;
    private readonly ICredentialStore _credentialStore;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;
    private readonly IAppLogger _appLogger;

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
    private string _quotaUsageText = "尚未加载存储容量";
    private bool _retentionEnabled;
    private string _retentionCountText = "50";
    private string _retentionDaysText = "0";
    private CloudGame? _selectedGame;
    private CloudSnapshotSummary? _selectedSnapshot;
    private DiscoveredGame? _selectedDiscoveredGame;
    private CloudDevice? _selectedDevice;

    public MainViewModel(
        SaveManifestBuilder manifestBuilder,
        IGameSaveApiClient apiClient,
        CloudSyncService cloudSyncService,
        SafeRestoreService safeRestoreService,
        IAutoSnapshotMonitor autoSnapshotMonitor,
        IGameDiscoveryService gameDiscoveryService,
        ILocalGameProfileStore localGameProfileStore,
        ICredentialStore credentialStore,
        IDeviceIdentityProvider deviceIdentityProvider,
        IAppLogger appLogger)
    {
        _manifestBuilder = manifestBuilder;
        _apiClient = apiClient;
        _cloudSyncService = cloudSyncService;
        _safeRestoreService = safeRestoreService;
        _autoSnapshotMonitor = autoSnapshotMonitor;
        _gameDiscoveryService = gameDiscoveryService;
        _localGameProfileStore = localGameProfileStore;
        _credentialStore = credentialStore;
        _deviceIdentityProvider = deviceIdentityProvider;
        _appLogger = appLogger;

        RegisterCommand = new AsyncCommand(() => AuthenticateAsync(true));
        LoginCommand = new AsyncCommand(() => AuthenticateAsync(false));
        CreateGameCommand = new AsyncCommand(CreateGameAsync);
        BuildManifestCommand = new AsyncCommand(BuildManifestAsync);
        SyncCommand = new AsyncCommand(SyncAsync);
        ReloadSnapshotsCommand = new AsyncCommand(ReloadSnapshotsFromUiAsync);
        DeleteSnapshotCommand = new AsyncCommand(DeleteSnapshotAsync);
        RestoreCommand = new AsyncCommand(RestoreAsync);
        StartAutoSnapshotCommand = new AsyncCommand(StartAutoSnapshotAsync);
        StopAutoSnapshotCommand = new AsyncCommand(StopAutoSnapshotAsync);
        DiscoverGamesCommand = new AsyncCommand(DiscoverGamesAsync);
        LoadLocalProfileCommand = new AsyncCommand(LoadLocalProfileFromUiAsync);
        ReloadDevicesCommand = new AsyncCommand(ReloadDevicesAsync);
        ReloadQuotaCommand = new AsyncCommand(ReloadQuotaAsync);
        ReloadRetentionCommand = new AsyncCommand(ReloadRetentionAsync);
        SaveRetentionCommand = new AsyncCommand(SaveRetentionAsync);
        CleanupRetentionCommand = new AsyncCommand(CleanupRetentionAsync);
        RevokeDeviceCommand = new AsyncCommand(RevokeDeviceAsync);
        KeepLocalConflictCommand = new AsyncCommand(KeepLocalConflictAsync);
        NavigateCommand = new DelegateCommand(NavigateTo);
        SelectGameCommand = new DelegateCommand(SelectGame);
        FilteredGames = CollectionViewSource.GetDefaultView(Games);
        FilteredGames.Filter = MatchesGameSearch;
        Games.CollectionChanged += (_, _) => FilteredGames.Refresh();
    }

    public ObservableCollection<CloudGame> Games { get; } = [];
    public ObservableCollection<CloudSnapshotSummary> Snapshots { get; } = [];
    public ObservableCollection<DiscoveredGame> DiscoveredGames { get; } = [];
    public ObservableCollection<CloudDevice> Devices { get; } = [];
    public ICollectionView FilteredGames { get; }

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
    public ICommand BuildManifestCommand { get; }
    public ICommand SyncCommand { get; }
    public ICommand ReloadSnapshotsCommand { get; }
    public ICommand DeleteSnapshotCommand { get; }
    public ICommand RestoreCommand { get; }
    public ICommand StartAutoSnapshotCommand { get; }
    public ICommand StopAutoSnapshotCommand { get; }
    public ICommand DiscoverGamesCommand { get; }
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

    public event EventHandler? PasswordClearRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>密码仅暂存于内存，并由 PasswordBox 调用此方法传入。</summary>
    public void SetPassword(string password) => _password = password;

    /// <summary>切换导航页面；每个页面复用同一份同步与本地配置状态。</summary>
    private void NavigateTo(object? page)
    {
        string target = page?.ToString() ?? "首页";
        CurrentPage = target;
        StatusText = target == "首页" ? "已返回同步概览。" : $"已切换到{target}。";
    }

    private void SelectGame(object? game)
    {
        if (game is not CloudGame selected) return;
        SelectedGame = selected;
        CurrentPage = "同步中心";
        StatusText = $"已选择{selected.Name}，请确认存档目录后同步。";
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
            await ReloadGamesAsync(server, session.DeviceToken);
            await ReloadDevicesAsync(server, session.DeviceToken);
            await ReloadQuotaAsync(server, session.DeviceToken);
            StatusText = $"认证成功，已加载 {Games.Count} 个云端游戏。";
        }
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
        if (SelectedGame is not null) { await RestoreLocalProfileAsync(server, token); await ReloadSnapshotsAsync(server, token); await ReloadRetentionAsync(server, token); }
    }

    private async Task RestoreLocalProfileAsync(Uri server, string token)
    {
        if (SelectedGame is null) return;
        string serverKey = GameSaveServerIdentity.CreateStableKey(server);
        LocalGameProfile? profile = await _localGameProfileStore.GetAsync(
            serverKey, SelectedGame.GameId, CancellationToken.None);
        if (profile is null) return;

        SaveDirectory = profile.SaveDirectory;
        AutoSnapshotProcessName = profile.ProcessName;
        if (!profile.AutoSnapshotEnabled
            || string.IsNullOrWhiteSpace(profile.ProcessName)
            || !Directory.Exists(profile.SaveDirectory)) return;

        string gameId = SelectedGame.GameId;
        string saveDirectory = profile.SaveDirectory;
        await _autoSnapshotMonitor.StartAsync(
            new AutoSnapshotProfile(profile.ProcessName, saveDirectory),
            cancellationToken => _cloudSyncService.SyncAsync(
                server, token, gameId, saveDirectory, SnapshotTrigger.GameExit, "游戏退出后自动快照", cancellationToken),
            CancellationToken.None);
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
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string provider = SelectedDiscoveredGame?.Provider ?? "CUSTOM";
            string? providerGameId = SelectedDiscoveredGame?.ProviderGameId;
            CloudGame game = await _apiClient.CreateGameAsync(server, token, NewGameName.Trim(), provider, providerGameId, CancellationToken.None);
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
            CloudSyncResult result = await _cloudSyncService.SyncAsync(
                server, token, SelectedGame.GameId, SaveDirectory, SnapshotTrigger.Manual, "手动同步", CancellationToken.None);
            FileCount = result.FileCount;
            LogicalSizeText = FormatBytes(result.LogicalSize);
            StatusText = result.Status == CloudSyncStatus.RemoteAhead
                ? result.Message + " 请从时间线选择云端快照恢复，或先处理本机版本冲突。"
                : result.Message;
            await ReloadSnapshotsAsync(server, token);
            await ReloadQuotaAsync(server, token);
        }
        catch (Exception exception) { ShowError("同步失败", exception); }
    }

    private async Task KeepLocalConflictAsync()
    {
        try
        {
            if (SelectedGame is null) throw new InvalidOperationException("请先选择云端游戏");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            CloudSyncResult result = await _cloudSyncService.SyncAsync(
                server, token, SelectedGame.GameId, SaveDirectory, SnapshotTrigger.Manual,
                "多设备冲突：保留本机版本", CancellationToken.None, keepLocalOnConflict: true);
            StatusText = $"已保留本机版本并创建快照 {result.SnapshotId}；原云端版本仍在时间线中。";
            await ReloadSnapshotsAsync(server, token);
            await ReloadQuotaAsync(server, token);
        }
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
            if (string.IsNullOrWhiteSpace(SaveDirectory)) throw new InvalidOperationException("请先填写本地存档目录");
            if (string.IsNullOrWhiteSpace(AutoSnapshotProcessName)) throw new InvalidOperationException("请填写游戏进程名，例如 eldenring.exe");
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync(server);
            string gameId = SelectedGame.GameId;
            string saveDirectory = SaveDirectory;
            await _autoSnapshotMonitor.StartAsync(
                new AutoSnapshotProfile(AutoSnapshotProcessName, saveDirectory),
                cancellationToken => _cloudSyncService.SyncAsync(
                    server, token, gameId, saveDirectory, SnapshotTrigger.GameExit, "游戏退出自动快照", cancellationToken),
                CancellationToken.None);
            await SaveLocalProfileAsync(server, true);
            StatusText = "自动快照已启用：目录变化只标记 dirty，游戏退出后才会同步。";
        }
        catch (Exception exception) { ShowError("启用自动快照失败", exception); }
    }

    private async Task StopAutoSnapshotAsync()
    {
        await _autoSnapshotMonitor.StopAsync();
        StatusText = "自动快照已停止。";
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