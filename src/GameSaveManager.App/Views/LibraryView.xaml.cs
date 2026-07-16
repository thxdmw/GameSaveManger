using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;

namespace GameSaveManager.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    private void OpenAddGameWizardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel)
            new AddGameWizardWindow(viewModel) { Owner = Window.GetWindow(this) }.ShowDialog();
    }

    private async void ChooseLocalGameExecutableButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "游戏程序 (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(Window.GetWindow(this)) != true || DataContext is not MainViewModel viewModel) return;
        try { await viewModel.AddLocalGameFromExecutableAsync(dialog.FileName); }
        catch (Exception exception)
        {
            ThemedDialogWindow.ShowThemed(Window.GetWindow(this), "选择本地游戏失败", exception.Message, "知道了");
        }
    }

    private static CloudGame? GetMenuGame(object sender) =>
        sender is MenuItem { DataContext: CloudGame game } ? game : null;

    private void GameMenuButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { ContextMenu: { } menu } button) return;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void ManageSaveMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMenuGame(sender) is not { } game || DataContext is not MainViewModel viewModel) return;
        if (viewModel.SelectGameCommand.CanExecute(game)) viewModel.SelectGameCommand.Execute(game);
        if (viewModel.NavigateCommand.CanExecute("同步中心")) viewModel.NavigateCommand.Execute("同步中心");
    }

    private void DeleteGameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (GetMenuGame(sender) is not { } game || DataContext is not MainViewModel viewModel) return;
        viewModel.SelectedGame = game;
        ThemedDialogResult confirmation = ThemedDialogWindow.ShowThemed(
            Window.GetWindow(this),
            "确认删除游戏",
            $"确定删除“{game.Name}”吗？\n\n这会删除该游戏的全部云端快照，以及这台电脑中保存的启动、存档和自动同步设置。此操作无法撤销；本机原始存档文件不会被删除。",
            "确认删除",
            "取消");
        if (confirmation == ThemedDialogResult.Primary && viewModel.DeleteGameCommand.CanExecute(null))
            viewModel.DeleteGameCommand.Execute(null);
    }
}
