using System.Windows;
using System.Windows.Input;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class AddGameWizardWindow : Window
{
    private bool _completed;

    public AddGameWizardWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        viewModel.BeginAddGameWizard();
        DataContext = viewModel.AddGameWizard;
        viewModel.GameCreated += ViewModel_OnGameCreated;
        Closed += (_, _) =>
        {
            viewModel.GameCreated -= ViewModel_OnGameCreated;
            viewModel.EndAddGameWizard(_completed);
        };
    }


    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();
    private async void ChooseLocalGameExecutableButton_OnClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "游戏启动入口 (*.exe;*.lnk)|*.exe;*.lnk", CheckFileExists = true };
        if (dialog.ShowDialog(this) != true || DataContext is not AddGameWizardViewModel wizard) return;
        await wizard.Host.AddLocalGameFromExecutableAsync(dialog.FileName);
    }

    private void ChooseSaveDirectoryButton_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择游戏存档目录",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            && DataContext is AddGameWizardViewModel wizard)
        {
            wizard.Host.SaveDirectory = dialog.SelectedPath;
        }
    }

    private async void TestLaunchButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameWizardViewModel wizard)
            await wizard.Host.TestPendingGameLaunchAsync();
    }

    private void PreviousButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameWizardViewModel wizard) wizard.Step--;
    }

    private void NextButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (DataContext is AddGameWizardViewModel wizard) wizard.Step++;
    }
    private void ViewModel_OnGameCreated(object? sender, EventArgs e)
    {
        _completed = true;
        if (IsVisible) DialogResult = true;
    }
}
