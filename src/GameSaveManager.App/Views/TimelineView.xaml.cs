using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;
using GameSaveManager.App.Common;

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

    private async void KeepLocalButton_OnClick(object sender, RoutedEventArgs e)
    {
        Window? owner = Window.GetWindow(this);
        if (DataContext is not MainViewModel viewModel || !viewModel.HasActiveConflict
            || viewModel.KeepLocalConflictCommand is not AsyncCommand command) return;
        int remoteFileCount = viewModel.Snapshots.FirstOrDefault(snapshot =>
            string.Equals(snapshot.SnapshotId, viewModel.ActiveConflictRemoteHeadSnapshotId, StringComparison.Ordinal))?.FileCount ?? 0;
        string riskWarning = viewModel.FileCount == 0 || (remoteFileCount > 0 && viewModel.FileCount * 2 <= remoteFileCount)
            ? $"\n\n高风险提示：本机只有 {viewModel.FileCount} 个文件，当前云端 HEAD 有 {remoteFileCount} 个文件。请确认本机目录没有选错、离线或被清空。"
            : $"\n\n本机 {viewModel.FileCount} 个文件；当前云端 HEAD {remoteFileCount} 个文件。";
        ThemedDialogResult confirmation = ThemedDialogWindow.ShowThemed(
            owner,
            "确认以本机版本为准",
            "这会将当前本机存档接到最新云端 HEAD 后创建新版本。请确认本机进度确实是要保留的版本；旧云端快照仍会保留。" + riskWarning,
            "保留本机并上传",
            "取消");
        if (confirmation == ThemedDialogResult.Primary) await command.ExecuteAsync(null);
    }
}
