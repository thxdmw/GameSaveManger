using System.Diagnostics;
using System.ComponentModel;
using System.Reflection;
using System.Windows;
using WpfApplication = GameSaveManager.App.App;
using GameSaveManager.App;
using GameSaveManager.App.Views;
using GameSaveManager.App.ViewModels;
using GameSaveManager.App.Theming;
using GameSaveManager.Application.Api;

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
            viewModel.Games.Add(new CloudGame("smoke-game", "界面冒烟游戏", "CUSTOM", null));
            var window = new MainWindow { DataContext = viewModel };
            var wizard = new AddGameWizardWindow(viewModel);
            var saveConfiguration = new SaveConfigurationDialog(viewModel.SaveConfiguration);
            var conflict = new ConflictResolutionDialog(viewModel);
            ThemeManager.Apply(useLightTheme: true);
            window.Measure(new Size(1100, 700));
            window.Arrange(new Rect(0, 0, 1100, 700));
            window.UpdateLayout();
            ThemeManager.Apply(useLightTheme: false);
            window.InvalidateVisual();
            window.UpdateLayout();
            wizard.Measure(new Size(700, 620));
            saveConfiguration.Measure(new Size(700, 600));
            conflict.Measure(new Size(620, 460));
            var dialog = new ThemedDialogWindow("验证", "验证主题对话框可以加载。", "确定", "取消");

            string[] errors = listener.Messages
                .Where(message => message.Contains("error", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            if (errors.Length > 0) throw new InvalidOperationException(string.Join(Environment.NewLine, errors));

            var closingArgs = new CancelEventArgs();
            MethodInfo closingHandler = typeof(MainWindow).GetMethod(
                "MainWindow_OnClosing", BindingFlags.Instance | BindingFlags.NonPublic)
                ?? throw new InvalidOperationException("未找到主窗口关闭处理方法。");
            closingHandler.Invoke(window, [window, closingArgs]);
            bool closePromptPending = (bool)(typeof(MainWindow).GetField(
                "_closePromptPending", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(window)
                ?? false);
            if (!closingArgs.Cancel || !closePromptPending)
                throw new InvalidOperationException(
                    "主窗口必须先取消 Closing，并延后显示退出确认，不得在 Closing 内重入关闭。");

            typeof(MainWindow).GetField("_allowClose", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(window, true);
            window.Close();
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
