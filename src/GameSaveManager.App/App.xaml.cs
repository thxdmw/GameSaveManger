using System.Windows;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Sync;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Security;

namespace GameSaveManager.App;

/// <summary>V2 客户端组合根：只在启动阶段组装具体基础设施实现。</summary>
public partial class App : Application
{
    private HttpClient? _httpClient;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var database = new SqliteDatabase();
            await database.InitializeAsync(CancellationToken.None);

            var manifestBuilder = new SaveManifestBuilder(
                new SaveDirectoryScanner(),
                new FileHashService(),
                new SqliteFileHashCache(database));

            _httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(30)
            };
            var apiClient = new GameSaveApiClient(_httpClient);
            var syncService = new CloudSyncService(
                manifestBuilder,
                apiClient,
                new SqliteSyncStateStore(database));

            var window = new MainWindow
            {
                DataContext = new MainViewModel(
                    manifestBuilder,
                    apiClient,
                    syncService,
                    new WindowsCredentialStore(),
                    new SqliteDeviceIdentityProvider(database))
            };
            window.Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                $"GameSave Manager V2 启动失败：{exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _httpClient?.Dispose();
        base.OnExit(e);
    }
}
