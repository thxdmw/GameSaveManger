using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;
using GameSaveManager.Application.Api;

namespace GameSaveManager.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    private void DeleteGameMenuItem_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { DataContext: CloudGame game } || DataContext is not MainViewModel viewModel) return;
        viewModel.SelectedGame = game;
        MessageBoxResult confirmation = MessageBox.Show(
            $"确定删除“{game.Name}”吗？\n\n这会删除该游戏的全部云端快照，并回收没有被其他快照引用的云端存档内容。此操作无法撤销；本机原始存档文件不会被删除。",
            "确认删除游戏", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (confirmation == MessageBoxResult.Yes && viewModel.DeleteGameCommand.CanExecute(null)) viewModel.DeleteGameCommand.Execute(null);
    }
}