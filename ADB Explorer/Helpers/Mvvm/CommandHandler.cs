namespace ADB_Explorer.Helpers;

public class CommandHandler : ICommand
{
    private readonly Action _action;
    private readonly Func<bool> _canExecute;

    /// <summary>
    /// Raises an event when the command is executed.
    /// </summary>
    public ObservableProperty<bool> OnExecute { get; set; } = new();

    public void Execute(object? parameter)
    {
        _action();

        OnExecute.Value ^= true;
    }

    public bool CanExecute(object? parameter)
    {
        return _canExecute.Invoke();
    }

    public CommandHandler(Action action, Func<bool> canExecute)
    {
        _action = action;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged
    {
        add => CommandManager.RequerySuggested += value;
        remove => CommandManager.RequerySuggested -= value;
    }

}

public class BaseAction : ObservableObject
{
    private readonly Func<bool> _canExecute;
    public bool IsEnabled => _canExecute();

    private readonly Action _action;

    private ICommand? _command;
    public ICommand Command => _command ??= new CommandHandler(_action, _canExecute);

    public BaseAction(Func<bool>? canExecute, Action action)
    {
        _canExecute = canExecute ??= () => true;
        _action = action;
    }

    public BaseAction()
    {
        _canExecute = () => true;
        _action = () => { };
    }

    public void Execute() => Command.Execute(null);

    public void NotifyIsEnabledChanged() => OnPropertyChanged(nameof(IsEnabled));
}
