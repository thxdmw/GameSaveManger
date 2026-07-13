using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

/// <summary>处理 PasswordBox 与视图模型之间的短暂密码传递。</summary>
public partial class SettingsView : UserControl
{
    private MainViewModel? _viewModel;

    public SettingsView()
    {
        InitializeComponent();
        DataContextChanged += SettingsView_OnDataContextChanged;
    }

    private void SettingsView_OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.PasswordClearRequested -= ClearPassword;
        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null) _viewModel.PasswordClearRequested += ClearPassword;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox)
        {
            viewModel.SetPassword(passwordBox.Password);
        }
    }

    private void ClearPassword(object? sender, EventArgs e) => PasswordInput.Clear();
}