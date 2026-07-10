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
}
