namespace ADB_Explorer.ViewModels;

public interface IDetailsViewModel
{
    string Label { get; }
    string Value { get; }
    bool ValueIsLtr { get; }
    bool ValueConsoleFont { get; }
    bool LabelConsoleFont { get; }
}

public class MountOptionViewModel(string option) : IDetailsViewModel
{
    private static readonly char[] separator = ['='];

    public string Label { get; } = option.Contains('=') ? option.Split(separator, 2)[0] : option;

    public string Value { get; } = option.Contains('=') ? option.Split(separator, 2)[1] : "";

    public bool ValueIsLtr { get; } = true;
    public bool ValueConsoleFont { get; } = true;
    public bool LabelConsoleFont { get; } = true;
}

public class ItemDetailsViewModel<T>(T item, string label, Func<T, string> valueSelector, bool valueIsLtr = false, bool useConsoleFont = false)
    : ObservableObject, IDetailsViewModel where T : INotifyPropertyChanged
{
    private readonly Func<T, string> _valueSelector = valueSelector;

    public string Label { get; } = label;

    public bool ValueIsLtr { get; } = valueIsLtr;

    public string Value => _valueSelector(item);

    public bool ValueConsoleFont { get; } = useConsoleFont;

    public bool LabelConsoleFont { get; } = false;

    public ItemDetailsViewModel<T> Init()
    {
        item.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Value));
        return this;
    }
}
