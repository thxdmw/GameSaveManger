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
        Closed += (_, _) => _trayIcon.Dispose();
    }
    private Forms.ContextMenuStrip CreateTrayMenu()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("打开 GameSave Manager", null, (_, _) => RestoreFromTray());
        menu.Items.Add("退出", null, (_, _) =>
        {
            _allowClose = true;
            _trayIcon.Visible = false;
            Close();
        });
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
        if (DataContext is MainViewModel viewModel) new Views.AddGameWizardWindow(viewModel) { Owner = this }.ShowDialog();
    }

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        Views.ThemedDialogResult choice = Views.ThemedDialogWindow.ShowThemed(
            this,
            "关闭 GameSave Manager",
            "你可以将程序最小化到系统托盘并继续自动同步，也可以完全退出程序。",
            "最小化到托盘",
            "退出程序",
            "取消");
        if (choice == Views.ThemedDialogResult.Primary)
        {
            e.Cancel = true;
            HideToTray();
        }
        else if (choice == Views.ThemedDialogResult.Cancel)
        {
            e.Cancel = true;
        }
        else
        {
            _allowClose = true;
            _trayIcon.Visible = false;
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
        }
        _subscribedViewModel = e.NewValue as MainViewModel;
        if (_subscribedViewModel is not null)
        {
            _subscribedViewModel.PasswordClearRequested += ViewModel_OnPasswordClearRequested;
            _subscribedViewModel.SyncConflictDetected += ViewModel_OnSyncConflictDetected;
            _subscribedViewModel.UpdateInstallationRequested += ViewModel_OnUpdateInstallationRequested;
        }
    }

    private void ViewModel_OnUpdateInstallationRequested(object? sender, EventArgs e)
    {
        if (sender is not MainViewModel viewModel) return;
        Views.ThemedDialogResult choice = Views.ThemedDialogWindow.ShowThemed(
            this,
            "安装客户端更新",
            "更新清单签名、文件摘要和 Windows 发布者签名均已验证。继续后客户端会完全退出，由安全更新程序安装新版本；如果新版本无法正常启动，会自动尝试恢复上一版本。",
            "退出并安装",
            null,
            "取消");
        if (choice != Views.ThemedDialogResult.Primary || !viewModel.TryLaunchPreparedUpdate()) return;
        _allowClose = true;
        _trayIcon.Visible = false;
        Close();
    }

    private void ViewModel_OnSyncConflictDetected(object? sender, EventArgs e)
    {
        if (sender is MainViewModel viewModel)
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
