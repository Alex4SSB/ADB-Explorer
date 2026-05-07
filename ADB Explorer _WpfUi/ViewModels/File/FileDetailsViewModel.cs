using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public class FileDetailsViewModel(FileClass file, string label, Func<FileClass, string> valueSelector, bool valueIsLtr = false)
    : FileViewModelBase(file)
{
    private readonly Func<FileClass, string> _valueSelector = valueSelector;

    public string Label { get; } = label;

    public bool ValueIsLtr { get; } = valueIsLtr;

    public string Value => _valueSelector(_file);
}
