using System.Windows;
using System.Windows.Input;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.App.Views;

public partial class SaveConfigurationDialog : Window
{
    public SaveConfigurationDialog(SaveConfigurationViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => Close();

    private void ChooseDirectory_OnClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择游戏存档目录",
            UseDescriptionForTitle = true
        };
        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK
            && DataContext is SaveConfigurationViewModel viewModel)
        {
            viewModel.SaveDirectory = dialog.SelectedPath;
        }
    }
}
