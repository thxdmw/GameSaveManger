using System.Diagnostics;
using System.Windows;
using WpfApplication = GameSaveManager.App.App;
using GameSaveManager.App;
using GameSaveManager.App.Views;
using GameSaveManager.App.ViewModels;

namespace GameSaveManager.Verification;

internal static class WpfSmokeVerification
{
    public static void VerifyMainWindowLoadsWithoutBindingErrors()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                VerifyOnStaThread();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw new InvalidOperationException($"WPF startup smoke test failed: {failure}", failure);
    }

    private static void VerifyOnStaThread()
    {
        using var listener = new BindingErrorListener();
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.All;
        PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
        try
        {
            var application = new WpfApplication();
            application.InitializeComponent();
            MainViewModel viewModel = SmokeViewModelFactory.Create();
            var window = new MainWindow { DataContext = viewModel };
            var wizard = new AddGameWizardWindow(viewModel);
            var dialog = new ThemedDialogWindow("验证", "验证主题对话框可以加载。", "确定", "取消");

            string[] errors = listener.Messages
                .Where(message => message.Contains("error", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));
        }
        finally
        {
            PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
        }
    }
    private sealed class BindingErrorListener : TraceListener
    {
        private readonly List<string> _messages = [];
        public IReadOnlyList<string> Messages => _messages;
        public override void Write(string? message) { }
        public override void WriteLine(string? message) => _messages.Add(message ?? string.Empty);
    }
}
