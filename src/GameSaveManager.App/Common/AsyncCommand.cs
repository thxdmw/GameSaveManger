using System.Windows.Input;

namespace GameSaveManager.App.Common;

/// <summary>异步命令，执行期间自动禁用，并支持业务条件和显式状态刷新。</summary>
public sealed class AsyncCommand : ICommand
{
    private readonly Func<object?, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    public static event EventHandler<AsyncCommandFailedEventArgs>? ExecutionFailed;
    public bool IsExecuting => _isExecuting;

    public AsyncCommand(Func<Task> execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute is null ? null : _ => canExecute()) { }

    public AsyncCommand(Func<object?, Task> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute;
        _canExecute = canExecute;
    }

    public bool CanExecute(object? parameter) => !_isExecuting && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter) => await ExecuteAsync(parameter);

    public async Task<bool> ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return false;
        _isExecuting = true;
        RaiseCanExecuteChanged();
        try
        {
            await _execute(parameter);
            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        catch (Exception exception)
        {
            ExecutionFailed?.Invoke(this, new AsyncCommandFailedEventArgs(exception));
            return false;
        }
        finally { _isExecuting = false; RaiseCanExecuteChanged(); }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    public event EventHandler? CanExecuteChanged;
}

public sealed class AsyncCommandFailedEventArgs(Exception exception) : EventArgs
{
    public Exception Exception { get; } = exception;
}
