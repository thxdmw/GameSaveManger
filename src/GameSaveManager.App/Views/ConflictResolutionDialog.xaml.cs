using System.Windows;
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
        _viewModel.SelectedSnapshot ??= _viewModel.Snapshots.FirstOrDefault();
    }

    private void RestoreRemote_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.RestoreCommand.CanExecute(null))
            _viewModel.RestoreCommand.Execute(null);
        DialogResult = true;
    }

    private void KeepLocal_OnClick(object sender, RoutedEventArgs e)
    {
        if (_viewModel.KeepLocalConflictCommand.CanExecute(null))
            _viewModel.KeepLocalConflictCommand.Execute(null);
        DialogResult = true;
    }
}
