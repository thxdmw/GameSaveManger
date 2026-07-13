using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class LibraryView : UserControl
{
    public LibraryView() => InitializeComponent();

    private void DeleteGameButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedGame is null)
        {
            MessageBox.Show("请先选择要删除的游戏。", "GameSave Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        MessageBoxResult confirmation = MessageBox.Show(
            $"确定删除“{viewModel.SelectedGame.Name}”吗？\n\n这会删除该游戏的全部云端快照，并回收没有被其他快照引用的云端存档内容。此操作无法撤销；本机原始存档文件不会被删除。",
            "确认删除游戏",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation == MessageBoxResult.Yes && viewModel.DeleteGameCommand.CanExecute(null)) viewModel.DeleteGameCommand.Execute(null);
    }
}