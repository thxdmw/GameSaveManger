using System.Net.Http;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Diagnostics;
using GameSaveManager.Application.Discovery;
using GameSaveManager.Application.Launching;
using GameSaveManager.Application.Games;
using GameSaveManager.Application.Monitoring;
using GameSaveManager.Application.Restores;
using GameSaveManager.Application.Security;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Application.Startup;
using GameSaveManager.Application.Sync;
using GameSaveManager.Infrastructure.Api;
using GameSaveManager.Infrastructure.Discovery;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Monitoring;
using GameSaveManager.Infrastructure.Launching;
using GameSaveManager.Infrastructure.Persistence;
using GameSaveManager.Infrastructure.Security;
using GameSaveManager.Infrastructure.Updates;

namespace GameSaveManager.Verification;

internal static class SmokeViewModelFactory
{
    public static MainViewModel Create(
        ILocalGameProfileStore? profileStore = null,
        IAutoSyncCoordinator? autoSyncCoordinator = null,
        ICredentialStore? credentialStore = null)
    {
        var database = new SqliteDatabase(Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", "smoke.db"));
        var fileHashService = new FileHashService();
        var manifestBuilder = new SaveManifestBuilder(
            new SaveDirectoryScanner(),
            fileHashService,
            new SqliteFileHashCache(database));
        var apiClient = new GameSaveApiClient(new HttpClient());
        var syncStateStore = new SqliteSyncStateStore(database);
        var pathTemplateService = new WindowsSavePathTemplateService();
        var syncService = new CloudSyncService(manifestBuilder, apiClient, syncStateStore, pathTemplateService);
        var registrySaveSnapshotService = new WindowsRegistrySaveSnapshotService();
        var restoreService = new SafeRestoreService(apiClient, new ContentObjectCache(fileHashService), fileHashService, syncStateStore, registrySaveSnapshotService);

        var viewModel = new MainViewModel(
            manifestBuilder,
            new SaveDirectoryPreviewService(new SaveDirectoryScanner()),
            apiClient,
            syncService,
            restoreService,
            autoSyncCoordinator ?? new MultiGameAutoSyncCoordinator(),
            new SnapshotExportService(apiClient, new ContentObjectCache(fileHashService), fileHashService),
            new WindowsGameDiscoveryService(),
            new WindowsSaveLocationDetector(),
            new WindowsExecutableGameIdentityFactory(),
            new WindowsRuntimeSaveLearningService(),
            new WindowsGameLaunchService(),
            new GameLaunchProfileMerger(),
            new WindowsShortcutResolver(),
            profileStore ?? new SqliteLocalGameProfileStore(database),
            credentialStore ?? new WindowsCredentialStore(),
            new SqliteDeviceIdentityProvider(database),
            pathTemplateService,
            new NullLogger(),
            new DisabledAutoStartService(),
            new TextFileServerAddressStore(Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", "smoke-server-address.txt")),
            new LudusaviManifestUpdateService(new HttpClient()),
            registrySaveSnapshotService,
            new GitHubClientUpdateService(
                new HttpClient(),
                Path.Combine(Path.GetTempPath(), "GameSaveManager.Verification", "updates")),
            new SqliteUpdatePreferenceStore(database));
        typeof(MainViewModel).GetField("_authenticatedUserId", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(viewModel, "smoke-user");
        typeof(MainViewModel).GetField("_isAuthenticated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
            .SetValue(viewModel, true);
        return viewModel;
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
