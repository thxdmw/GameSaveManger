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

    private void DeleteGameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: CloudGame game } || DataContext is not MainViewModel viewModel) return;
        viewModel.SelectedGame = game;
        ThemedDialogResult confirmation = ThemedDialogWindow.ShowThemed(
            Window.GetWindow(this),
            "确认删除游戏",
            $"确定删除“{game.Name}”吗？\n\n这会删除该游戏的全部云端快照，并回收没有被其他快照引用的云端存档内容。此操作无法撤销；本机原始存档文件不会被删除。",
            "确认删除",
            "取消");
        if (confirmation == ThemedDialogResult.Primary && viewModel.DeleteGameCommand.CanExecute(null))
            viewModel.DeleteGameCommand.Execute(null);
    }
}