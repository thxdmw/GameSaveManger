using System.Windows;
using System.Windows.Input;
using System.ComponentModel;
using GameSaveManager.App.Common;
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
        Closing += AddGameWizardWindow_OnClosing;
        Closed += (_, _) =>
        {
            viewModel.GameCreated -= ViewModel_OnGameCreated;
            viewModel.EndAddGameWizard(_completed);
        };
    }

    private void AddGameWizardWindow_OnClosing(object? sender, CancelEventArgs e)
    {
        if (_completed || DataContext is not AddGameWizardViewModel wizard) return;
        if (wizard.Host.CreateGameCommand is not AsyncCommand { IsExecuting: true }) return;
        e.Cancel = true;
        ThemedDialogWindow.ShowThemed(
            this,
            "正在创建游戏",
            "创建请求正在确认服务端结果，暂时不能关闭窗口。完成后窗口会自动关闭；失败时可以修改后重试。",
            "知道了");
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
        try { await wizard.Host.AddLocalGameFromExecutableAsync(dialog.FileName); }
        catch (OperationCanceledException) { }
        catch (Exception exception)
        {
            ThemedDialogWindow.ShowThemed(this, "选择本地游戏失败", exception.Message, "知道了");
        }
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
        if (DataContext is AddGameWizardViewModel wizard) wizard.TryMoveNext();
    }
    private void ViewModel_OnGameCreated(object? sender, EventArgs e)
    {
        _completed = true;
        if (IsVisible) DialogResult = true;
    }
}
