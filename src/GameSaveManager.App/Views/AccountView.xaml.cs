using System;
using System.Windows;
using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

/// <summary>账户页只负责 PasswordBox 与 ViewModel 的安全适配。</summary>
public partial class AccountView : System.Windows.Controls.UserControl
{
    private MainViewModel? _viewModel;

    public AccountView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null) _viewModel.PasswordClearRequested -= ClearPassword;
        _viewModel = e.NewValue as MainViewModel;
        if (_viewModel is not null) _viewModel.PasswordClearRequested += ClearPassword;
    }

    private void PasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel viewModel && sender is PasswordBox passwordBox) viewModel.SetPassword(passwordBox.Password);
    }

    private void ClearPassword(object? sender, EventArgs e) => PasswordInput.Clear();
}