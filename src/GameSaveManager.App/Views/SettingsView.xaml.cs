using System.Windows.Controls;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class SettingsView : System.Windows.Controls.UserControl
{
    public SettingsView() => InitializeComponent();
    private void ShowThirdPartyNotice_OnClick(object sender, System.Windows.RoutedEventArgs e) => new ThirdPartyNoticeWindow { Owner = System.Windows.Application.Current.MainWindow }.ShowDialog();

    private void ThemeSelection_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not ComboBox comboBox || comboBox.SelectedIndex < 0) return;
        bool useLightTheme = comboBox.SelectedIndex == 1;
        if (useLightTheme != viewModel.IsLightTheme && viewModel.ToggleThemeCommand.CanExecute(null)) viewModel.ToggleThemeCommand.Execute(null);
    }

    private void AutoStartSelection_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel || sender is not ComboBox comboBox || comboBox.SelectedIndex < 0) return;
        bool enable = comboBox.SelectedIndex == 1;
        if (enable != viewModel.AutoStartEnabled && viewModel.ToggleAutoStartCommand.CanExecute(null)) viewModel.ToggleAutoStartCommand.Execute(null);
    }
}