namespace ADB_Explorer.ViewModels;

public abstract class ViewModelBase : ObservableObject
{
    protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    public static void ExecuteInDispatcher(Action action, bool executeInDispatcher = true)
    {
        if (App.IsShuttingDown || App.AppDispatcher is null || executeInDispatcher)
            action();
        else
            App.AppDispatcher.Invoke(action);
    }
}
