using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class GameDetailView : UserControl
{
    public GameDetailView() => InitializeComponent();

    private async void ChooseLaunchExecutableButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedGame is null) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "游戏可执行文件 (*.exe)|*.exe",
            CheckFileExists = true,
            InitialDirectory = File.Exists(viewModel.AutoSnapshotExecutablePath)
                ? Path.GetDirectoryName(viewModel.AutoSnapshotExecutablePath)
                : null
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            await viewModel.SetAutoSnapshotExecutablePathAsync(dialog.FileName);
    }
}