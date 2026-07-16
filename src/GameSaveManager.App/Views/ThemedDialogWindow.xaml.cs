using System.Windows;
using System.Windows.Input;

namespace GameSaveManager.App.Views;

public enum ThemedDialogResult
{
    Cancel,
    Primary,
    Secondary
}

public partial class ThemedDialogWindow : Window
{
    public ThemedDialogResult Result { get; private set; } = ThemedDialogResult.Cancel;

    public ThemedDialogWindow(
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        string? cancelText = null)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        PrimaryActionButton.Content = primaryText;
        ConfigureOptionalButton(SecondaryActionButton, secondaryText);
        ConfigureOptionalButton(CancelActionButton, cancelText);
    }

    public static ThemedDialogResult ShowThemed(
        Window? owner,
        string title,
        string message,
        string primaryText,
        string? secondaryText = null,
        string? cancelText = null)
    {
        var dialog = new ThemedDialogWindow(title, message, primaryText, secondaryText, cancelText);
        if (owner is { IsVisible: true }) dialog.Owner = owner;
        else dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dialog.ShowDialog();
        return dialog.Result;
    }

    private static void ConfigureOptionalButton(FrameworkElement button, string? text)
    {
        button.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
        if (button is System.Windows.Controls.ContentControl contentControl) contentControl.Content = text;
    }

    private void PrimaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Result = ThemedDialogResult.Primary;
        DialogResult = true;
    }

    private void SecondaryButton_OnClick(object sender, RoutedEventArgs e)
    {
        Result = ThemedDialogResult.Secondary;
        DialogResult = true;
    }

    private void CancelButton_OnClick(object sender, RoutedEventArgs e)
    {
        Result = ThemedDialogResult.Cancel;
        DialogResult = false;
    }

    private void TitleBar_OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
