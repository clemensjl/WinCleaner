using System.Windows.Input;

namespace WinCleaner.Gui.Mvvm;

/// <summary>
/// Asynchroner ICommand: führt eine Task aus und blockt sich selbst währenddessen
/// (verhindert Doppelklicks/parallele Läufe). Ausnahmen werden an einen optionalen
/// Handler gemeldet statt zu verschlucken.
/// </summary>
public sealed class AsyncRelayCommand : ICommand
{
    private readonly Func<Task> _execute;
    private readonly Func<bool>? _canExecute;
    private readonly Action<Exception>? _onError;
    private bool _running;

    public AsyncRelayCommand(Func<Task> execute, Func<bool>? canExecute = null, Action<Exception>? onError = null)
    {
        _execute = execute;
        _canExecute = canExecute;
        _onError = onError;
    }

    public bool CanExecute(object? parameter) => !_running && (_canExecute?.Invoke() ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;
        _running = true;
        RaiseCanExecuteChanged();
        try { await _execute(); }
        catch (Exception ex) { _onError?.Invoke(ex); }
        finally
        {
            _running = false;
            RaiseCanExecuteChanged();
        }
    }

    public event EventHandler? CanExecuteChanged
    {
        add    => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

    public void RaiseCanExecuteChanged() => CommandManager.InvalidateRequerySuggested();
}
