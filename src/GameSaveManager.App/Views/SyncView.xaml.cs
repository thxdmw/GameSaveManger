using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;
using Forms = System.Windows.Forms;

namespace GameSaveManager.App.Views;

public partial class SyncView : UserControl
{
    public SyncView() => InitializeComponent();

    private void GameSelection_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel
            || sender is not ComboBox { SelectedItem: CloudGame game } comboBox
            || (!comboBox.IsDropDownOpen && !comboBox.IsKeyboardFocusWithin)) return;
        if (viewModel.SelectGameCommand.CanExecute(game)) viewModel.SelectGameCommand.Execute(game);
    }

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

    private async void ChooseProcessExecutableButton_OnClick(object sender, RoutedEventArgs e)
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
            await viewModel.SetAutoSnapshotExecutablePathAsync(dialog.FileName);
        }
    }
}
