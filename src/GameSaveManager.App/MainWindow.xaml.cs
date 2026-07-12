using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App;

/// <summary>主窗口仅保留 PasswordBox 的 UI 适配，不承载任何业务逻辑。</summary>
public partial class MainWindow : Window
{
    private MainViewModel? _subscribedViewModel;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_OnDataContextChanged;
    }

    private void MainWindow_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PasswordClearRequested -= ViewModel_OnPasswordClearRequested;
        }

        _subscribedViewModel = e.NewValue as MainViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PasswordClearRequested += ViewModel_OnPasswordClearRequested;
        }
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.SetPassword(passwordBox.Password);
        }
    }

    private void ViewModel_OnPasswordClearRequested(object? sender, EventArgs e)
    {
        PasswordInput.Clear();
    }
    /// <summary>删除快照前由界面收集一次明确确认，业务删除仍由 ViewModel 命令执行。</summary>
    private void DeleteSnapshotButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedSnapshot is null)
        {
            MessageBox.Show(this, "请先在时间线中选择要删除的历史快照。", "GameSave Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirmation = MessageBox.Show(
            this,
            "删除后，该快照将不再出现在时间线中；未被其他快照引用的内容会进入云端清理流程。当前同步 HEAD 无法删除。是否继续？",
            "确认删除历史快照",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (confirmation == MessageBoxResult.Yes && viewModel.DeleteSnapshotCommand.CanExecute(null))
        {
            viewModel.DeleteSnapshotCommand.Execute(null);
        }
    }
}
