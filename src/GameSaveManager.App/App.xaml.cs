using System.Net.Http;
using System.Windows;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Diagnostics;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Monitoring;
using GameSaveManager.Application.Restores;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Sync;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.Diagnostics;
using GameSaveManager.Infrastructure.Discovery;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Monitoring;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Security;

namespace GameSaveManager.App;

/// <summary>客户端组合根；仅在启动阶段组装基础设施实现。</summary>
public partial class App : System.Windows.Application
{
    private HttpClient? _httpClient;
    private IAutoSnapshotMonitor? _autoSnapshotMonitor;
    private IAppLogger? _appLogger;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            _appLogger = new JsonFileLogger();
            _appLogger.Information("application.starting", "客户端正在启动。");
            DispatcherUnhandledException += (_, args) =>
                _appLogger.Error("application.unhandled_ui_exception", args.Exception, "未处理的界面线程异常。");
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                if (args.ExceptionObject is Exception exception)
                {
                    _appLogger.Error("application.unhandled_exception", exception, "未处理的进程异常。");
                }
            };

            var database = new SqliteDatabase();
            await database.InitializeAsync(CancellationToken.None);
            var fileHashService = new FileHashService();
            var manifestBuilder = new SaveManifestBuilder(
                new SaveDirectoryScanner(),
                fileHashService,
                new SqliteFileHashCache(database));

            _httpClient = new HttpClient(new SafeRetryHandler(new HttpClientHandler()))
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            var apiClient = new GameSaveApiClient(_httpClient);
            var syncService = new CloudSyncService(
                manifestBuilder,
                apiClient,
                new SqliteSyncStateStore(database));
            var restoreService = new SafeRestoreService(
                apiClient,
                new ContentObjectCache(fileHashService),
                fileHashService);
            _autoSnapshotMonitor = new WindowsAutoSnapshotMonitor();

            IReadOnlyList<string> recoveryMessages = await restoreService.RecoverInterruptedRestoresAsync(
                CancellationToken.None);
            if (recoveryMessages.Count > 0)
            {
                MessageBox.Show(
                    string.Join(Environment.NewLine, recoveryMessages),
                    "存档恢复检查",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            var window = new MainWindow
            {
                DataContext = new MainViewModel(
                    manifestBuilder,
                    apiClient,
                    syncService,
                    restoreService,
                    _autoSnapshotMonitor,
                    new WindowsGameDiscoveryService(),
                    new SqliteLocalGameProfileStore(database),
                    new WindowsCredentialStore(),
                    new SqliteDeviceIdentityProvider(database),
                    _appLogger)
            };
            window.Show();
        }
        catch (Exception exception)
        {
            _appLogger?.Error("application.startup_failed", exception, "客户端启动失败。");
            MessageBox.Show(
                $"GameSave Manager 启动失败：{exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_autoSnapshotMonitor is not null)
        {
            await _autoSnapshotMonitor.DisposeAsync();
        }
        _httpClient?.Dispose();
        _appLogger?.Information("application.stopped", "客户端已退出。");
        base.OnExit(e);
    }
}