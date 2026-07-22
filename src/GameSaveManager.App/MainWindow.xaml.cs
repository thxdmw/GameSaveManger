using System.ComponentModel;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GameSaveManager.App.ViewModels;
using Forms = System.Windows.Forms;

namespace GameSaveManager.App;

/// <summary>主窗口只承载窗口交互、托盘与 PasswordBox 的安全适配。</summary>
public partial class MainWindow : Window
{
    private const int WmNcLeftButtonDown = 0x00A1;
    private readonly Forms.NotifyIcon _trayIcon;
    private MainViewModel? _subscribedViewModel;
    private bool _allowClose;
    private bool _shutdownInProgress;
    private bool _closePromptPending;
    private int _notificationGeneration;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += MainWindow_OnDataContextChanged;
        Closing += MainWindow_OnClosing;
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? SystemIcons.Application,
            Text = "GameSave Manager",
            Visible = false
        };
        _trayIcon.ContextMenuStrip = CreateTrayMenu();
        _trayIcon.DoubleClick += (_, _) => RestoreFromTray();
        Closed += (_, _) =>
        {
            Interlocked.Increment(ref _notificationGeneration);
            _trayIcon.Dispose();
        };
    }
    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 GameSave Manager", null, (_, _) => RestoreFromTray());
        menu.Items.Add("退出", null, async (_, _) => await CompleteShutdownAndCloseAsync());
        return menu;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) ToggleMaximize();
        else if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SendMessage(nint hWnd, int message, nint wParam, nint lParam);

    private void ResizeBorder_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (WindowState == WindowState.Maximized || sender is not FrameworkElement { Tag: string tag } || !int.TryParse(tag, out int hitTest)) return;
        ReleaseCapture();
        SendMessage(new WindowInteropHelper(this).Handle, WmNcLeftButtonDown, hitTest, 0);
    }

    private void MinimizeButton_OnClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void MaximizeButton_OnClick(object sender, RoutedEventArgs e) => ToggleMaximize();
    private void ToggleMaximize() => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void OpenAddGameWizardButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel { IsAuthenticated: true } viewModel)
            new Views.AddGameWizardWindow(viewModel) { Owner = this }.ShowDialog();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        e.Cancel = true;
        if (_shutdownInProgress || _closePromptPending) return;

        // WPF 禁止在 Closing 回调内再次 Show/Hide/Close。先取消本次关闭，
        // 待事件返回后再询问用户并执行真正的退出流程。
        _closePromptPending = true;
        _ = Dispatcher.InvokeAsync(PromptForClose);
    }

    private void PromptForClose()
    {
        try
        {
            if (_allowClose || _shutdownInProgress) return;
            Views.ThemedDialogResult choice = Views.ThemedDialogWindow.ShowThemed(
                this,
                "关闭 GameSave Manager",
                "你可以将程序最小化到系统托盘并继续自动同步，也可以完全退出程序。",
                "最小化到托盘",
                "退出程序",
                "取消");
            if (choice == Views.ThemedDialogResult.Primary)
                HideToTray();
            else if (choice == Views.ThemedDialogResult.Secondary)
                _ = CompleteShutdownAndCloseAsync();
        }
        finally
        {
            _closePromptPending = false;
        }
    }

    private async Task<bool> CompleteShutdownAndCloseAsync()
    {
        if (_shutdownInProgress) return false;
        _shutdownInProgress = true;
        IsEnabled = false;
        try
        {
            if (DataContext is MainViewModel viewModel)
                await viewModel.PrepareForShutdownAsync();
            _allowClose = true;
            _trayIcon.Visible = false;
            Close();
            return true;
        }
        catch (Exception exception)
        {
            _allowClose = false;
            if (DataContext is MainViewModel viewModel)
                await viewModel.ResumeAfterCancelledShutdownAsync();
            if (!IsVisible) _trayIcon.Visible = true;
            Views.ThemedDialogWindow.ShowThemed(
                this,
                "退出前清理失败",
                $"无法安全停止正在进行的存档任务：{exception.Message}。客户端将继续运行，请稍后重试。",
                "知道了");
            return false;
        }
        finally
        {
            if (!_allowClose)
            {
                IsEnabled = true;
                _shutdownInProgress = false;
            }
        }
    }

    private void HideToTray()
    {
        Hide();
        _trayIcon.Visible = true;
        _trayIcon.ShowBalloonTip(1500, "GameSave Manager", "程序仍在后台运行，自动同步会继续执行。双击托盘图标可恢复窗口。", Forms.ToolTipIcon.Info);
    }

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        _trayIcon.Visible = false;
    }

    private void MainWindow_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PasswordClearRequested -= ViewModel_OnPasswordClearRequested;
            _subscribedViewModel.SyncConflictDetected -= ViewModel_OnSyncConflictDetected;
            _subscribedViewModel.UpdateInstallationRequested -= ViewModel_OnUpdateInstallationRequested;
            _subscribedViewModel.WindowsNotificationRequested -= ViewModel_OnWindowsNotificationRequested;
            _subscribedViewModel.UserConfirmationRequested -= ViewModel_OnUserConfirmationRequested;
        }
        _subscribedViewModel = e.NewValue as MainViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PasswordClearRequested += ViewModel_OnPasswordClearRequested;
            _subscribedViewModel.SyncConflictDetected += ViewModel_OnSyncConflictDetected;
            _subscribedViewModel.UpdateInstallationRequested += ViewModel_OnUpdateInstallationRequested;
            _subscribedViewModel.WindowsNotificationRequested += ViewModel_OnWindowsNotificationRequested;
            _subscribedViewModel.UserConfirmationRequested += ViewModel_OnUserConfirmationRequested;
        }
    }

    private void ViewModel_OnUserConfirmationRequested(object? sender, UserConfirmationEventArgs e)
    {
        if (_shutdownInProgress || _allowClose)
        {
            e.Confirmed = false;
            return;
        }
        e.Confirmed = Views.ThemedDialogWindow.ShowThemed(
            this,
            e.Title,
            e.Message,
            e.ConfirmText,
            "取消") == Views.ThemedDialogResult.Primary;
    }

    private void ViewModel_OnWindowsNotificationRequested(object? sender, WindowsNotificationEventArgs e)
    {
        void ShowNotification()
        {
            if (_shutdownInProgress || _allowClose) return;
            try
            {
                bool keepVisible = !IsVisible || _trayIcon.Visible;
                int generation = Interlocked.Increment(ref _notificationGeneration);
                _trayIcon.Visible = true;
                Forms.ToolTipIcon icon = e.Kind switch
                {
                    WindowsNotificationKind.Error => Forms.ToolTipIcon.Error,
                    WindowsNotificationKind.Warning => Forms.ToolTipIcon.Warning,
                    _ => Forms.ToolTipIcon.Info
                };
                _trayIcon.ShowBalloonTip(
                    5000,
                    TruncateNotificationText(e.Title, 63),
                    TruncateNotificationText(e.Message, 255),
                    icon);
                if (!keepVisible) _ = HideTransientTrayIconAsync(generation);
            }
            catch (Exception exception) when (exception is InvalidOperationException
                                                       or ArgumentException)
            {
                System.Diagnostics.Trace.TraceWarning($"Windows 通知显示失败：{exception.GetType().Name}");
            }
        }
        if (_shutdownInProgress || _allowClose
            || Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished) return;
        if (Dispatcher.CheckAccess()) ShowNotification();
        else _ = Dispatcher.InvokeAsync(ShowNotification);
    }

    private async Task HideTransientTrayIconAsync(int generation)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(6));
            if (generation == Volatile.Read(ref _notificationGeneration)
                && !_allowClose && IsVisible && WindowState != WindowState.Minimized)
                _trayIcon.Visible = false;
        }
        catch (ObjectDisposedException) { }
    }

    private static string TruncateNotificationText(string? value, int maximumLength)
    {
        string normalized = string.IsNullOrWhiteSpace(value) ? "GameSave Manager" : value.Trim();
        return normalized.Length <= maximumLength
            ? normalized
            : normalized[..(maximumLength - 1)] + "…";
    }

    private async void ViewModel_OnUpdateInstallationRequested(object? sender, EventArgs e)
    {
        if (sender is not MainViewModel viewModel) return;
        Views.ThemedDialogResult choice = Views.ThemedDialogWindow.ShowThemed(
            this,
            "安装客户端更新",
            "更新清单签名、文件摘要和 Windows 发布者签名均已验证。继续后客户端会完全退出，由安全更新程序安装新版本；如果新版本无法正常启动，会自动尝试恢复上一版本。",
            "退出并安装",
            null,
            "取消");
        if (choice != Views.ThemedDialogResult.Primary || _shutdownInProgress) return;
        _shutdownInProgress = true;
        IsEnabled = false;
        try
        {
            if (!await viewModel.PrepareForShutdownAsync())
            {
                await viewModel.ResumeAfterCancelledShutdownAsync();
                return;
            }
            if (!viewModel.TryLaunchPreparedUpdate())
            {
                await viewModel.ResumeAfterCancelledShutdownAsync();
                return;
            }
            _allowClose = true;
            _trayIcon.Visible = false;
            Close();
        }
        catch (Exception exception)
        {
            _allowClose = false;
            await viewModel.ResumeAfterCancelledShutdownAsync();
            if (!IsVisible) _trayIcon.Visible = true;
            Views.ThemedDialogWindow.ShowThemed(
                this,
                "安装更新失败",
                $"无法安全停止客户端并启动更新：{exception.Message}",
                "知道了");
        }
        finally
        {
            if (!_allowClose)
            {
                IsEnabled = true;
                _shutdownInProgress = false;
            }
        }
    }

    private void ViewModel_OnSyncConflictDetected(object? sender, SyncConflictEventArgs e)
    {
        if (!_shutdownInProgress && !_allowClose
            && sender is MainViewModel viewModel
            && string.Equals(viewModel.SelectedGame?.GameId, e.GameId, StringComparison.Ordinal))
            new Views.ConflictResolutionDialog(viewModel) { Owner = this }.ShowDialog();
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox) viewModel.SetPassword(passwordBox.Password);
    }

    private void ViewModel_OnPasswordClearRequested(object? sender, EventArgs e)
    {
        if (FindName("PasswordInput") is PasswordBox passwordInput) passwordInput.Clear();
    }

    private void DeleteSnapshotButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || viewModel.SelectedSnapshot is null)
        {
            Views.ThemedDialogWindow.ShowThemed(this, "GameSave Manager", "请先在时间线中选择要删除的历史快照。", "知道了");
            return;
        }
        if (Views.ThemedDialogWindow.ShowThemed(this, "确认删除历史快照", "删除后，该快照将不再出现在时间线中；未被其他快照引用的内容会进入云端清理流程。当前同步 HEAD 无法删除。", "确认删除", "取消") == Views.ThemedDialogResult.Primary && viewModel.DeleteSnapshotCommand.CanExecute(null)) viewModel.DeleteSnapshotCommand.Execute(null);
    }
}
