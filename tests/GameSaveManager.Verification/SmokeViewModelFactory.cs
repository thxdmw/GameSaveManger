using System.Net.Http;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Diagnostics;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Monitoring;
using GameSaveManager.Application.Restores;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Startup;
using GameSaveManager.Application.Sync;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.Discovery;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Monitoring;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Security;

namespace GameSaveManager.Verification;

internal static class SmokeViewModelFactory
{
    public static MainViewModel Create()
    {
        var database = new SqliteDatabase(Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", "smoke.db"));
        var fileHashService = new FileHashService();
        var manifestBuilder = new SaveManifestBuilder(
            new SaveDirectoryScanner(),
            fileHashService,
            new SqliteFileHashCache(database));
        var apiClient = new GameSaveApiClient(new HttpClient());
        var syncStateStore = new SqliteSyncStateStore(database);
        var syncService = new CloudSyncService(manifestBuilder, apiClient, syncStateStore);
        var restoreService = new SafeRestoreService(apiClient, new ContentObjectCache(fileHashService), fileHashService, syncStateStore);

        return new MainViewModel(
            manifestBuilder,
            apiClient,
            syncService,
            restoreService,
            new MultiGameAutoSyncCoordinator(),
            new SnapshotExportService(apiClient, new ContentObjectCache(fileHashService), fileHashService),
            new WindowsGameDiscoveryService(),
            new WindowsSavePathSuggestionService(),
            new SqliteLocalGameProfileStore(database),
            new WindowsCredentialStore(),
            new SqliteDeviceIdentityProvider(database),
            new NullLogger(),
            new DisabledAutoStartService());
    }

    private sealed class NullLogger : IAppLogger
    {
        public void Information(string eventName, string message) { }
        public void Error(string eventName, Exception exception, string message) { }
    }

    private sealed class DisabledAutoStartService : IAutoStartService
    {
        public bool IsEnabled() => false;
        public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
