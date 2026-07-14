using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;
using Forms = System.Windows.Forms;

namespace GameSaveManager.App.Views;

public partial class SyncView : UserControl
{
    public SyncView() => InitializeComponent();

    private void ChooseSaveDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog
        {
            Description = "选择游戏存档目录",
            UseDescriptionForTitle = true,
            SelectedPath = (DataContext as MainViewModel)?.SaveDirectory ?? string.Empty
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK && DataContext is MainViewModel viewModel)
        {
            viewModel.SaveDirectory = dialog.SelectedPath;
        }
    }

    private void ChooseProcessExecutableButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.OpenFileDialog
        {
            Filter = "Executable files (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false,
            Title = "选择游戏可执行文件"
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK && DataContext is MainViewModel viewModel)
        {
            viewModel.AutoSnapshotProcessName = System.IO.Path.GetFileName(dialog.FileName);
        }
    }
}