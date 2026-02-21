namespace ADB_Explorer.ViewModels;

public abstract class ViewModelBase : ObservableObject, INotifyPropertyChanged
{
    public event PropertyChangedEventHandler PropertyChanged;

    protected virtual bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (Equals(storage, value))
        {
            return false;
        }

        storage = value;
        OnPropertyChanged(propertyName);

        return true;
    }

    protected void OnPropertyChanged([CallerMemberName] string propertyName = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public static void ExecuteInDispatcher(Action action, bool executeInDispatcher = true)
    {
        // When running unit tests, App.Current is null
        if (App.Current is null || executeInDispatcher)
            action();
        else
            App.Current.Dispatcher.Invoke(action);
    }
}
