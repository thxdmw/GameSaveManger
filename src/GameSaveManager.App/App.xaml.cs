using System.Net.Http;
using System.Windows;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Diagnostics;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Monitoring;
using GameSaveManager.Application.Launching;
using GameSaveManager.Application.Restores;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Sync;
using GameSaveManager.Application.Updates;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.Diagnostics;
using GameSaveManager.Infrastructure.Discovery;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Monitoring;
using GameSaveManager.Infrastructure.Launching;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Security;
using GameSaveManager.Infrastructure.Startup;
using GameSaveManager.Infrastructure.Updates;

namespace GameSaveManager.App;

/// <summary>客户端组合根；仅在启动阶段组装基础设施实现。</summary>
public partial class App : System.Windows.Application
{
    private HttpClient? _httpClient;
    private IAutoSyncCoordinator? _autoSyncCoordinator;
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
            var syncStateStore = new SqliteSyncStateStore(database);
            var syncService = new CloudSyncService(
                manifestBuilder,
                apiClient,
                syncStateStore);
            var registrySaveSnapshotService = new WindowsRegistrySaveSnapshotService();
            var restoreService = new SafeRestoreService(
                apiClient,
                new ContentObjectCache(fileHashService),
                fileHashService,
                syncStateStore,
                registrySaveSnapshotService);
            _autoSyncCoordinator = new MultiGameAutoSyncCoordinator();

            IReadOnlyList<string> recoveryMessages = await restoreService.RecoverInterruptedRestoresAsync(
                CancellationToken.None);
            if (recoveryMessages.Count > 0)
            {
                Views.ThemedDialogWindow.ShowThemed(null, "存档恢复检查", string.Join(Environment.NewLine, recoveryMessages), "知道了");
            }

            var window = new MainWindow
            {
                DataContext = new MainViewModel(
                    manifestBuilder,
                    new SaveDirectoryPreviewService(new SaveDirectoryScanner()),
                    apiClient,
                    syncService,
                    restoreService,
                    _autoSyncCoordinator,
                    new SnapshotExportService(apiClient, new ContentObjectCache(fileHashService), fileHashService),
                    new WindowsGameDiscoveryService(),
                    new WindowsSaveLocationDetector(),
                    new WindowsExecutableGameIdentityFactory(),
                    new WindowsRuntimeSaveLearningService(),
                    new WindowsGameLaunchService(),
                    new GameLaunchProfileMerger(),
                    new WindowsShortcutResolver(),
                    new SqliteLocalGameProfileStore(database),
                    new WindowsCredentialStore(),
                    new SqliteDeviceIdentityProvider(database),
                    _appLogger,
                    new WindowsAutoStartService(),
                    new TextFileServerAddressStore(),
                    new LudusaviManifestUpdateService(_httpClient),
                    registrySaveSnapshotService,
                    new GitHubClientUpdateService(_httpClient),
                    new SqliteUpdatePreferenceStore(database))
            };
            window.Show();
            if (window.DataContext is MainViewModel viewModel) await viewModel.InitializeAsync();
        }
        catch (Exception exception)
        {
            _appLogger?.Error("application.startup_failed", exception, "客户端启动失败。");
            Views.ThemedDialogWindow.ShowThemed(null, "启动失败", $"GameSave Manager 启动失败：{exception.Message}", "退出");
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_autoSyncCoordinator is not null)
        {
            await _autoSyncCoordinator.DisposeAsync();
        }
        _httpClient?.Dispose();
        _appLogger?.Information("application.stopped", "客户端已退出。");
        base.OnExit(e);
    }
}
