using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class TimelineView : UserControl
{
    public TimelineView() => InitializeComponent();

    private void DeleteSnapshotButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedSnapshot is null)
        {
            MessageBox.Show("请先选择要删除的历史快照。", "GameSave Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show("删除后该快照将不再出现在时间线中，未被其他快照引用的内容会进入云端清理流程。当前同步 HEAD 无法删除。是否继续？", "确认删除历史快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes && viewModel.DeleteSnapshotCommand.CanExecute(null)) viewModel.DeleteSnapshotCommand.Execute(null);
    }

    private void RestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedSnapshot is null)
        {
            MessageBox.Show("请先选择要恢复的快照。", "GameSave Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show("恢复会覆盖当前存档。程序会先创建当前存档的安全备份，是否继续？", "确认恢复存档", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes && viewModel.RestoreCommand.CanExecute(null)) viewModel.RestoreCommand.Execute(null);
    }
}