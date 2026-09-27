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
    private static readonly char[] _separator = ['='];

    public string Label { get; } = option.Contains('=') ? option.Split(_separator, 2)[0] : option;

    public string Value { get; } = option.Contains('=') ? option.Split(_separator, 2)[1] : "";

    public bool ValueIsLtr { get; } = true;
    public bool ValueConsoleFont { get; } = true;
    public bool LabelConsoleFont { get; } = true;
    public Visibility RowVisibility { get; } = Visibility.Visible;
}

/// <summary>
/// One of a file's three date rows, labeled whatever it holds. Its value is left blank when the file's mount
/// doesn't tell that date, or when the modified date already shows the same.
/// </summary>
public class DateDetailsViewModel : ObservableObject, IDetailsViewModel
{
    public enum DateKind
    {
        Modified,
        Accessed,
        Created,
    }

    private readonly FileClass _file;
    private readonly DateKind _kind;
    private readonly bool _applies;

    public DateDetailsViewModel(FileClass file, DateKind kind, bool applies)
    {
        _file = file;
        _kind = kind;
        _applies = applies;

        file.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(FileClass.ModifiedTime)
                or nameof(FileClass.ModifiedTimeWithOffset)
                or nameof(FileClass.LastAccessTime)
                or nameof(FileClass.CreationTime))
                OnPropertyChanged(nameof(Value));
        };
    }

    public string Label => _kind switch
    {
        DateKind.Modified => Strings.Resources.S_FILE_INFO_MODIFIED,
        DateKind.Accessed => Strings.Resources.S_DATE_ACCESSED,
        _ => Strings.Resources.S_CREATION_TIME,
    };

    public string Value
    {
        get
        {
            var view = _file.FolderViewModel;
            var modified = _file.ModifiedTime.HasValue ? view.ModifiedTimeWithOffsetString : "";

            if (_kind is DateKind.Modified)
                return modified;

            if (!_applies)
                return "";

            var text = "";
            if (_kind is DateKind.Accessed && _file.LastAccessTime.HasValue)
                text = view.LastAccessTimeString;
            else if (_kind is DateKind.Created && _file.CreationTime.HasValue)
                text = view.CreationTimeString;

            if (text == modified)
                return "";

            return text;
        }
    }

    public bool ValueIsLtr => true;

    public bool ValueConsoleFont => false;

    public bool LabelConsoleFont => false;

    public Visibility RowVisibility => Visibility.Visible;
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

    /// <summary>Keeps the row current while the item changes - only the item properties it's derived from are followed.</summary>
    public ItemDetailsViewModel<T> Init(string dependsOn, params string[] moreDependsOn)
    {
        item.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName != dependsOn && !moreDependsOn.Contains(e.PropertyName))
                return;

            OnPropertyChanged(nameof(Value));
            if (_rowVisibility is not null)
                OnPropertyChanged(nameof(RowVisibility));
        };
        return this;
    }
}
