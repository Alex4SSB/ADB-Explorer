using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public interface IDetailsViewModel
{
    string Label { get; }
    string Value { get; }
    bool ValueIsLtr { get; }
    bool UseConsoleFont { get; }
}

public class FileDetailsViewModel(FileClass file, string label, Func<FileClass, string> valueSelector, bool valueIsLtr = false, bool useConsoleFont = false, Func<FileClass, bool>? visibilityPredicate = null)
    : FileViewModelBase(file), IDetailsViewModel
{
    private readonly Func<FileClass, string> _valueSelector = valueSelector;

    public string Label { get; } = label;

    public bool ValueIsLtr { get; } = valueIsLtr;

    public string Value => _valueSelector(_file);

    public bool UseConsoleFont { get; } = useConsoleFont;

    public FileDetailsViewModel Init()
    {
        file.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Value));
        return this;
    }
}

public class PackageDetailsViewModel(Package package, string label, Func<Package, string> valueSelector, bool valueIsLtr = false, bool useConsoleFont = false) 
    : ObservableObject, IDetailsViewModel
{
    private readonly Func<Package, string> _valueSelector = valueSelector;

    public string Label { get; } = label;

    public bool ValueIsLtr { get; } = valueIsLtr;

    public string Value => _valueSelector(package);

    public bool UseConsoleFont { get; } = useConsoleFont;

    public PackageDetailsViewModel Init()
    {
        package.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Value));
        return this;
    }
}
