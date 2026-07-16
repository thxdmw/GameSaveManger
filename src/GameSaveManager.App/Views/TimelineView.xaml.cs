using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class TimelineView : UserControl
{
    public TimelineView() => InitializeComponent();

    private void DeleteSnapshotButton_OnClick(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedSnapshot is null)
        {
            ThemedDialogWindow.ShowThemed(owner, "GameSave Manager", "请先选择要删除的历史快照。", "知道了");
            return;
        }
        ThemedDialogResult confirmation = ThemedDialogWindow.ShowThemed(
            owner,
            "确认删除历史快照",
            "删除后该快照将不再出现在时间线中，未被其他快照引用的内容会进入云端清理流程。当前同步 HEAD 无法删除。",
            "确认删除",
            "取消");
        if (confirmation == ThemedDialogResult.Primary && viewModel.DeleteSnapshotCommand.CanExecute(null))
            viewModel.DeleteSnapshotCommand.Execute(null);
    }

    private void RestoreButton_OnClick(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedSnapshot is null)
        {
            ThemedDialogWindow.ShowThemed(owner, "GameSave Manager", "请先选择要恢复的快照。", "知道了");
            return;
        }
        ThemedDialogResult confirmation = ThemedDialogWindow.ShowThemed(
            owner,
            "确认恢复存档",
            "恢复会覆盖当前存档。程序会先创建当前存档的安全备份，是否继续？",
            "确认恢复",
            "取消");
        if (confirmation == ThemedDialogResult.Primary && viewModel.RestoreCommand.CanExecute(null))
            viewModel.RestoreCommand.Execute(null);
    }
}