using System.Windows.Input;

namespace GameSaveManager.App.Common;

/// <summary>用于页面切换等同步 UI 操作的最小命令实现。</summary>
public sealed class DelegateCommand(Action<object?> execute) : ICommand
{
    public bool CanExecute(object? parameter) => true;

    public void Execute(object? parameter) => execute(parameter);

    public event EventHandler? CanExecuteChanged
    {
        add { }
        remove { }
    }
}
