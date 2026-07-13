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

    private void MainWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose) return;
        MessageBoxResult choice = MessageBox.Show(
            this,
            "选择“是”将最小化到系统托盘并继续自动同步；选择“否”将退出程序。",
            "关闭 GameSave Manager",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);
        if (choice == MessageBoxResult.Yes)
        {
            e.Cancel = true;
            HideToTray();
        }
        else if (choice == MessageBoxResult.Cancel)
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
        if (_subscribedViewModel is not null) _subscribedViewModel.PasswordClearRequested -= ViewModel_OnPasswordClearRequested;
        _subscribedViewModel = e.NewValue as MainViewModel;
        if (_subscribedViewModel is not null) _subscribedViewModel.PasswordClearRequested += ViewModel_OnPasswordClearRequested;
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
            MessageBox.Show(this, "请先在时间线中选择要删除的历史快照。", "GameSave Manager", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (MessageBox.Show(this, "删除后，该快照将不再出现在时间线中；未被其他快照引用的内容会进入云端清理流程。当前同步 HEAD 无法删除。是否继续？", "确认删除历史快照", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes && viewModel.DeleteSnapshotCommand.CanExecute(null)) viewModel.DeleteSnapshotCommand.Execute(null);
    }
}