using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public interface IDetailsViewModel
{
    string Label { get; }
    string Value { get; }
    bool ValueIsLtr { get; }
}

public class FileDetailsViewModel(FileClass file, string label, Func<FileClass, string> valueSelector, bool valueIsLtr = false)
    : FileViewModelBase(file), IDetailsViewModel
{
    private readonly Func<FileClass, string> _valueSelector = valueSelector;

    public string Label { get; } = label;

    public bool ValueIsLtr { get; } = valueIsLtr;

    public string Value => _valueSelector(_file);
}

public class PackageDetailsViewModel(Package package, string label, Func<Package, string> valueSelector, bool valueIsLtr = false) : ObservableObject, IDetailsViewModel
{
    private readonly Func<Package, string> _valueSelector = valueSelector;

    public string Label { get; } = label;

    public bool ValueIsLtr { get; } = valueIsLtr;

    public string Value => _valueSelector(package);

    public PackageDetailsViewModel Init()
    {
        package.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Value));
        return this;
    }
}
