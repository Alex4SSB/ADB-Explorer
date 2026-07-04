namespace ADB_Explorer.ViewModels;

using System.Windows;

public interface IDetailsViewModel
{
    string Label { get; }
    string Value { get; }
    bool ValueIsLtr { get; }
    bool ValueConsoleFont { get; }
    bool LabelConsoleFont { get; }
    Visibility RowVisibility { get; }
}

public class MountOptionViewModel(string option) : IDetailsViewModel
{
    private static readonly char[] separator = ['='];

    public string Label { get; } = option.Contains('=') ? option.Split(separator, 2)[0] : option;

    public string Value { get; } = option.Contains('=') ? option.Split(separator, 2)[1] : "";

    public bool ValueIsLtr { get; } = true;
    public bool ValueConsoleFont { get; } = true;
    public bool LabelConsoleFont { get; } = true;
    public Visibility RowVisibility { get; } = Visibility.Visible;
}

public class ItemDetailsViewModel<T>(T item, string label, Func<T, string> valueSelector, bool valueIsLtr = false, bool useConsoleFont = false, Func<T, Visibility>? rowVisibility = null)
    : ObservableObject, IDetailsViewModel where T : INotifyPropertyChanged
{
    private readonly Func<T, string> _valueSelector = valueSelector;
    private readonly Func<T, Visibility>? _rowVisibility = rowVisibility;

    public string Label { get; } = label;

    public bool ValueIsLtr { get; } = valueIsLtr;

    public string Value => _valueSelector(item);

    public bool ValueConsoleFont { get; } = useConsoleFont;

    public bool LabelConsoleFont { get; } = false;

    public Visibility RowVisibility => _rowVisibility?.Invoke(item)
        ?? (string.IsNullOrEmpty(Value) ? Visibility.Collapsed : Visibility.Visible);

    public ItemDetailsViewModel<T> Init()
    {
        item.PropertyChanged += (s, e) =>
        {
            OnPropertyChanged(nameof(Value));
            if (_rowVisibility is not null)
                OnPropertyChanged(nameof(RowVisibility));
        };
        return this;
    }
}
