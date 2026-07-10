using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using GameSaveManager.App.Common;
using GameSaveManager.Application.Api;
using GameSaveManager.Application.Device;
using GameSaveManager.Application.Security;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Sync;
using GameSaveManager.Domain.Snapshots;

namespace GameSaveManager.App.ViewModels;

/// <summary>V2 第一阶段主窗口状态与操作编排；文件和网络业务均委托给 Application 层。</summary>
public sealed class MainViewModel : INotifyPropertyChanged
{
    private readonly SaveManifestBuilder _manifestBuilder;
    private readonly IGameSaveApiClient _apiClient;
    private readonly CloudSyncService _cloudSyncService;
    private readonly ICredentialStore _credentialStore;
    private readonly IDeviceIdentityProvider _deviceIdentityProvider;

    private string _serverAddress = "http://localhost:8080";
    private string _username = string.Empty;
    private string _password = string.Empty;
    private string _newGameName = string.Empty;
    private string _saveDirectory = string.Empty;
    private string _statusText = "先注册或登录，然后选择云端游戏并配置本机存档目录。";
    private int _fileCount;
    private string _logicalSizeText = "0 B";
    private CloudGame? _selectedGame;

    public MainViewModel(
        SaveManifestBuilder manifestBuilder,
        IGameSaveApiClient apiClient,
        CloudSyncService cloudSyncService,
        ICredentialStore credentialStore,
        IDeviceIdentityProvider deviceIdentityProvider)
    {
        _manifestBuilder = manifestBuilder;
        _apiClient = apiClient;
        _cloudSyncService = cloudSyncService;
        _credentialStore = credentialStore;
        _deviceIdentityProvider = deviceIdentityProvider;

        RegisterCommand = new AsyncCommand(RegisterAsync);
        LoginCommand = new AsyncCommand(LoginAsync);
        CreateGameCommand = new AsyncCommand(CreateGameAsync);
        BuildManifestCommand = new AsyncCommand(BuildManifestAsync);
        SyncCommand = new AsyncCommand(SyncAsync);
    }

    public ObservableCollection<CloudGame> Games { get; } = [];

    public string ServerAddress
    {
        get => _serverAddress;
        set => SetField(ref _serverAddress, value);
    }

    public string Username
    {
        get => _username;
        set => SetField(ref _username, value);
    }

    public string NewGameName
    {
        get => _newGameName;
        set => SetField(ref _newGameName, value);
    }

    public string SaveDirectory
    {
        get => _saveDirectory;
        set => SetField(ref _saveDirectory, value);
    }

    public string StatusText
    {
        get => _statusText;
        private set => SetField(ref _statusText, value);
    }

    public int FileCount
    {
        get => _fileCount;
        private set => SetField(ref _fileCount, value);
    }

    public string LogicalSizeText
    {
        get => _logicalSizeText;
        private set => SetField(ref _logicalSizeText, value);
    }

    public CloudGame? SelectedGame
    {
        get => _selectedGame;
        set => SetField(ref _selectedGame, value);
    }

    public ICommand RegisterCommand { get; }
    public ICommand LoginCommand { get; }
    public ICommand CreateGameCommand { get; }
    public ICommand BuildManifestCommand { get; }
    public ICommand SyncCommand { get; }

    /// <summary>PasswordBox 通过薄 UI 适配层更新密码；密码不会写入 SQLite。</summary>
    public void SetPassword(string password) => _password = password;

    private Task RegisterAsync() => AuthenticateAsync(register: true);

    private Task LoginAsync() => AuthenticateAsync(register: false);

    private async Task AuthenticateAsync(bool register)
    {
        try
        {
            Uri server = ParseServerUri();
            string deviceId = await _deviceIdentityProvider.GetOrCreateDeviceIdAsync(CancellationToken.None);
            StatusText = register ? "正在创建账号并登记当前设备…" : "正在登录并轮换当前设备 Token…";

            AuthSession session = register
                ? await _apiClient.RegisterAsync(
                    server, Username, _password, deviceId, Environment.MachineName, CancellationToken.None)
                : await _apiClient.LoginAsync(
                    server, Username, _password, deviceId, Environment.MachineName, CancellationToken.None);

            await _credentialStore.SaveAsync(
                CredentialTargets.DeviceToken,
                session.DeviceToken,
                CancellationToken.None);
            await ReloadGamesAsync(server, session.DeviceToken);
            StatusText = $"认证成功。当前设备：{session.DeviceId}，已加载 {Games.Count} 个云端游戏。";
        }
        catch (Exception exception)
        {
            ShowError(register ? "注册失败" : "登录失败", exception);
        }
    }

    private async Task ReloadGamesAsync(Uri server, string deviceToken)
    {
        IReadOnlyList<CloudGame> games = await _apiClient.ListGamesAsync(
            server, deviceToken, CancellationToken.None);
        Games.Clear();
        foreach (CloudGame game in games)
        {
            Games.Add(game);
        }
        SelectedGame = Games.FirstOrDefault();
    }

    private async Task CreateGameAsync()
    {
        try
        {
            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync();
            if (string.IsNullOrWhiteSpace(NewGameName))
            {
                throw new InvalidOperationException("请先输入游戏名称");
            }

            CloudGame game = await _apiClient.CreateGameAsync(
                server,
                token,
                NewGameName.Trim(),
                "CUSTOM",
                null,
                CancellationToken.None);
            await ReloadGamesAsync(server, token);
            SelectedGame = Games.FirstOrDefault(item => item.GameId == game.GameId);
            NewGameName = string.Empty;
            StatusText = $"已创建云端游戏：{game.Name}。现在可以填写本机存档目录。";
        }
        catch (Exception exception)
        {
            ShowError("创建游戏失败", exception);
        }
    }

    private async Task BuildManifestAsync()
    {
        try
        {
            StatusText = "正在完整扫描目录并计算变化文件的 SHA-256…";
            IReadOnlyList<SnapshotFile> files = await _manifestBuilder.BuildAsync(
                SaveDirectory,
                CancellationToken.None);
            UpdateManifestSummary(files);
            StatusText = "Manifest 已构建；Hash Cache 已持久化到本地 SQLite。";
        }
        catch (Exception exception)
        {
            ShowError("扫描失败", exception);
        }
    }

    private async Task SyncAsync()
    {
        try
        {
            if (SelectedGame is null)
            {
                throw new InvalidOperationException("请先选择云端游戏");
            }

            Uri server = ParseServerUri();
            string token = await RequireDeviceTokenAsync();
            StatusText = "正在检查本机 HEAD、云端 HEAD 和缺失内容对象…";
            CloudSyncResult result = await _cloudSyncService.SyncAsync(
                server,
                token,
                SelectedGame.GameId,
                SaveDirectory,
                SnapshotTrigger.Manual,
                "手动同步",
                CancellationToken.None);

            FileCount = result.FileCount;
            LogicalSizeText = FormatBytes(result.LogicalSize);
            StatusText = result.Message;
        }
        catch (Exception exception)
        {
            ShowError("同步失败", exception);
        }
    }

    private async Task<string> RequireDeviceTokenAsync()
    {
        string? token = await _credentialStore.ReadAsync(
            CredentialTargets.DeviceToken,
            CancellationToken.None);
        return string.IsNullOrWhiteSpace(token)
            ? throw new InvalidOperationException("当前没有设备 Token，请先注册或登录")
            : token;
    }

    private Uri ParseServerUri()
    {
        if (!Uri.TryCreate(ServerAddress.Trim(), UriKind.Absolute, out Uri? uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("服务器地址必须是有效的 http/https URL");
        }
        return uri;
    }

    private void UpdateManifestSummary(IReadOnlyList<SnapshotFile> files)
    {
        FileCount = files.Count;
        LogicalSizeText = FormatBytes(files.Sum(file => file.Size));
    }

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
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
        {
            return false;
        }
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
