using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameSaveManager.App.Common;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Device;
using GameSaveManager.Application.Discovery;
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
    private readonly ICredentialStore _credentialStore;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;

    private string _serverAddress = "http://localhost:8080";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _newGameName = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _autoSnapshotProcessName = string.Empty;
    private string _statusText = "请先注册或登录，然后选择云端游戏并配置本地存档目录。";
    private int _fileCount;
    private string _logicalSizeText = "0 B";
    private CloudGame? _selectedGame;
    private CloudSnapshotSummary? _selectedSnapshot;
    private DiscoveredGame? _selectedDiscoveredGame;

    public MainViewModel(
        SaveManifestBuilder manifestBuilder,
        IGameSaveApiClient apiClient,
        CloudSyncService cloudSyncService,
        SafeRestoreService safeRestoreService,
        IAutoSnapshotMonitor autoSnapshotMonitor,
        IGameDiscoveryService gameDiscoveryService,
        ICredentialStore credentialStore,
        IDeviceIdentityProvider deviceIdentityProvider)
    {
        _manifestBuilder = manifestBuilder;
        _apiClient = apiClient;
        _cloudSyncService = cloudSyncService;
        _safeRestoreService = safeRestoreService;
        _autoSnapshotMonitor = autoSnapshotMonitor;
        _gameDiscoveryService = gameDiscoveryService;
        _credentialStore = credentialStore;
        _deviceIdentityProvider = deviceIdentityProvider;

        RegisterCommand = new AsyncCommand(() => AuthenticateAsync(true));
        LoginCommand = new AsyncCommand(() => AuthenticateAsync(false));
        CreateGameCommand = new AsyncCommand(CreateGameAsync);
        BuildManifestCommand = new AsyncCommand(BuildManifestAsync);
        SyncCommand = new AsyncCommand(SyncAsync);
        ReloadSnapshotsCommand = new AsyncCommand(ReloadSnapshotsFromUiAsync);
        RestoreCommand = new AsyncCommand(RestoreAsync);
        StartAutoSnapshotCommand = new AsyncCommand(StartAutoSnapshotAsync);
        StopAutoSnapshotCommand = new AsyncCommand(StopAutoSnapshotAsync);
        DiscoverGamesCommand = new AsyncCommand(DiscoverGamesAsync);
        KeepLocalConflictCommand = new AsyncCommand(KeepLocalConflictAsync);
    }

    public ObservableCollection<CloudGame> Games { get; } = [];
    public ObservableCollection<CloudSnapshotSummary> Snapshots { get; } = [];
    public ObservableCollection<DiscoveredGame> DiscoveredGames { get; } = [];

    public string ServerAddress { get => _serverAddress; set => SetField(ref _serverAddress, value); }
    public string Username { get => _username; set => SetField(ref _username, value); }
    public string NewGameName { get => _newGameName; set => SetField(ref _newGameName, value); }
    public string SaveDirectory { get => _saveDirectory; set => SetField(ref _saveDirectory, value); }
    public string AutoSnapshotProcessName { get => _autoSnapshotProcessName; set => SetField(ref _autoSnapshotProcessName, value); }
    public string StatusText { get => _statusText; private set => SetField(ref _statusText, value); }
    public int FileCount { get => _fileCount; private set => SetField(ref _fileCount, value); }
    public string LogicalSizeText { get => _logicalSizeText; private set => SetField(ref _logicalSizeText, value); }

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
    public ICommand RestoreCommand { get; }
    public ICommand StartAutoSnapshotCommand { get; }
    public ICommand StopAutoSnapshotCommand { get; }
    public ICommand DiscoverGamesCommand { get; }
    public ICommand KeepLocalConflictCommand { get; }

    public event EventHandler? PasswordClearRequested;
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>密码仅暂存于内存，并由 PasswordBox 调用此方法传入。</summary>
    public void SetPassword(string password) => _password = password;

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
        if (SelectedGame is not null) await ReloadSnapshotsAsync(server, token);
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