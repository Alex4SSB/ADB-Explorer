using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public static class FileOpFilters
{
    private static List<FileOpFilter> list = null;
    public static List<FileOpFilter> List
    {
        get
        {
            if (list is null)
            {
                list = new(Enum.GetValues<FileOpFilter.FilterType>().Select(f => new FileOpFilter(f)));

                UpdateCheckedColumns();
            }

            return list;
        }
    }

    public static ObservableProperty<int> CheckedFilterCount { get; private set; } = new() { Value = -1 };

    public static void UpdateCheckedColumns() =>
        CheckedFilterCount.Value = List.Count(col => col.IsChecked is true);
}

public class FileOpFilter : ViewModelBase
{
    public enum FilterType
    {
        Running,
        Pending,
        Completed,
        Validated,
        Failed,
        Canceled,
        Previous,
    }

    private bool? isChecked = null;
    public bool? IsChecked
    {
        get => isChecked;
        set
        {
            if (Set(ref isChecked, value))
            {
                if (FileOpFilters.CheckedFilterCount > 0)
                    FileOpFilters.UpdateCheckedColumns();

                Store();
            }
        }
    }
    
    private CheckBox checkBox = null;
    public CheckBox CheckBox
    {
        get
        {
            if (checkBox is null)
            {
                checkBox = new CheckBox()
                {
                    Style = (Style)App.Current.FindResource("FileOpFilterCheckBox"),
                    DataContext = this,
                    Content = AdbExplorerConst.FILE_OP_NAMES[Type],
                    Margin = new(0, -6, 0, -6),
                };
            }

            return checkBox;
        }
    }

    public FilterType Type { get; }

    public bool IsEnabled => IsChecked is false || FileOpFilters.CheckedFilterCount > 1;

    public FileOpFilter(FilterType type)
    {
        Type = type;

        IsChecked = Retrieve() is bool val ? val : true;

        FileOpFilters.CheckedFilterCount.PropertyChanged += (object sender, PropertyChangedEventArgs<int> e) => OnPropertyChanged(nameof(IsEnabled));
    }

    private bool? Retrieve() => Storage.RetrieveBool($"{nameof(FilterType)}_{Type}");

    private void Store() => Storage.StoreValue($"{nameof(FilterType)}_{Type}", $"{IsChecked}");
}
