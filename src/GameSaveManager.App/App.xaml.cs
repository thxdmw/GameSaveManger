using System.Windows;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Snapshots;
using GameSaveManager.Infrastructure.FileSystem;
using GameSaveManager.Infrastructure.Persistence;

namespace GameSaveManager.App;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var manifestBuilder = new SaveManifestBuilder(
            new SaveDirectoryScanner(),
            new FileHashService(),
            new InMemoryFileHashCache());

        var window = new MainWindow
        {
            DataContext = new MainViewModel(manifestBuilder)
        };
        window.Show();
    }
}
