using System.Windows;
using GameSaveManager.App.Common;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class ConflictResolutionDialog : Window
{
    private readonly MainViewModel _viewModel;

    public ConflictResolutionDialog(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        _viewModel.SelectedSnapshot = _viewModel.Snapshots.FirstOrDefault(snapshot =>
                                          string.Equals(snapshot.SnapshotId, _viewModel.ActiveConflictRemoteHeadSnapshotId, StringComparison.Ordinal))
                                      ?? _viewModel.Snapshots.FirstOrDefault();
    }

    private async void RestoreRemote_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.RestoreCommand is AsyncCommand command
            && await command.ExecuteAsync(null)
            && !_viewModel.HasActiveConflict)
            DialogResult = true;
    }

    private async void KeepLocal_OnClick(object sender, RoutedEventArgs e)
    {
        int remoteFileCount = _viewModel.SelectedSnapshot?.FileCount ?? 0;
        string riskWarning = _viewModel.FileCount == 0 || (remoteFileCount > 0 && _viewModel.FileCount * 2 <= remoteFileCount)
            ? $"\n\n高风险提示：本机只有 {_viewModel.FileCount} 个文件，当前云端 HEAD 有 {remoteFileCount} 个文件。请确认本机目录没有选错、离线或被清空。"
            : $"\n\n本机 {_viewModel.FileCount} 个文件；当前云端 HEAD {remoteFileCount} 个文件。";
        ThemedDialogResult confirmation = ThemedDialogWindow.ShowThemed(
            this,
            "确认以本机版本为准",
            "这会把当前本机存档接到最新云端 HEAD 后创建新版本。请确认本机进度确实是要保留的版本；旧云端快照仍会保留在时间线中。" + riskWarning,
            "保留本机并上传",
            "取消");
        if (confirmation != ThemedDialogResult.Primary) return;
        if (_viewModel.KeepLocalConflictCommand is AsyncCommand command
            && await command.ExecuteAsync(null)
            && !_viewModel.HasActiveConflict)
            DialogResult = true;
    }
}
