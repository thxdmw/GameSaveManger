using System.Windows;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class AddGameWizardWindow : Window
{
    public AddGameWizardWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        viewModel.GameCreated += ViewModel_OnGameCreated;
        Closed += (_, _) => viewModel.GameCreated -= ViewModel_OnGameCreated;
    }

    private async void ChooseLocalGameExecutableButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "游戏启动入口 (*.exe;*.lnk)|*.exe;*.lnk", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || DataContext is not MainViewModel viewModel) return;
        await viewModel.AddLocalGameFromExecutableAsync(dialog.FileName);
    }
    private void ViewModel_OnGameCreated(object? sender, EventArgs e)
    {
        if (IsVisible) DialogResult = true;
    }
}
