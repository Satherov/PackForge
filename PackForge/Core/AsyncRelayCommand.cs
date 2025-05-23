using System;
using System.Threading.Tasks;
using System.Windows.Input;
using Serilog;

namespace PackForge.Core;

public class AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null) : ICommand
{
    private readonly Func<Task> _execute = execute ?? throw new ArgumentNullException(nameof(execute));

    public bool CanExecute(object? parameter)
    {
        return canExecute?.Invoke() ?? true;
    }

    public async void Execute(object? parameter)
    {
        try
        {
            await _execute();
            await Task.Yield();
        }
        catch (OperationCanceledException)
        {
            Log.Warning("Operation was killed by the user");
        }
        catch (Exception ex)
        {
            Log.Fatal("AsyncRelayCommand experienced a fatal Error. Report this to the Author: {Exception}", ex);
        }
    }

    public event EventHandler? CanExecuteChanged;

    public void RaiseCanExecuteChanged()
    {
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}