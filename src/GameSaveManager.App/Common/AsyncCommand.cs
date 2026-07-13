using System.Windows.Input;

namespace GameSaveManager.App.Common;

/// <summary>最小异步 ICommand 实现；执行期间自动禁用，防止用户重复点击同一操作。</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private bool _isExecuting;

    public AsyncCommand(Func<Task> execute) : this(_ => execute()) { }

    public AsyncCommand(Func<object?, Task> execute) => _execute = execute;

    public bool CanExecute(object? parameter) => !_isExecuting;

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _isExecuting = true;
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        try { await _execute(parameter); }
        finally { _isExecuting = false; CanExecuteChanged?.Invoke(this, EventArgs.Empty); }
    }

    public event EventHandler? CanExecuteChanged;
}