namespace ADB_Explorer.Helpers;

public class PropertyChangedEventArgs<T> : EventArgs
{
    public T OldValue { get; set; }
    public T NewValue { get; set; }
}

public class ObservableProperty<T>
{
    public event EventHandler<PropertyChangedEventArgs<T>> PropertyChanged;

    private T _value;
    public T Value
    {
        get => _value;
        set
        {
            if (Equals(_value, value))
                return;

            PropertyChangedEventArgs<T> args = new()
            {
                OldValue = _value,
                NewValue = value
            };

            _value = value;
            PropertyChanged?.Invoke(this, args);
        }
    }

    public static implicit operator T(ObservableProperty<T> p) => p.Value;
}
