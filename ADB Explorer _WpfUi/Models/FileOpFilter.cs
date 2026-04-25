using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public static class FileOpFilters
{
    public static List<FileOpFilter> FilterList
    {
        get
        {
            if (field is null)
            {
                var filterTypes = Enum.GetValues<FileOpFilter.FilterType>().Where(f => f is not FileOpFilter.FilterType.Running);
                if (Data.Settings.FileOpFilters.Length == 0)
                    Data.Settings.FileOpFilters = [.. filterTypes];

                field = [.. filterTypes.Select(f => new FileOpFilter(f))];

                UpdateCheckedFilters();
            }

            return field;
        }
    } = null;

    public static ObservableProperty<int> CheckedFilterCount { get; private set; } = new() { Value = -1 };

    public static void UpdateCheckedFilters() =>
        CheckedFilterCount.Value = FilterList.Count(col => col.IsChecked is true);
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

    private bool isChecked = true;
    public bool IsChecked
    {
        get => isChecked;
        set
        {
            if (Set(ref isChecked, value))
            {
                var filters = Data.Settings.FileOpFilters.Where(f => f != Type);
                if (value)
                    filters = [.. filters, Type];

                Data.Settings.FileOpFilters = [.. filters];

                if (FileOpFilters.CheckedFilterCount > 0)
                    FileOpFilters.UpdateCheckedFilters();
            }
        }
    }

    public static string GetFilterName(FilterType filterType)
        => filterType switch
    {
        FilterType.Running => "",
        FilterType.Pending => Strings.Resources.S_FILEOP_WAITING,
        FilterType.Completed => Strings.Resources.S_FILEOP_COMPLETED,
        FilterType.Validated => Strings.Resources.S_FILEOP_VALIDATED,
        FilterType.Failed => Strings.Resources.S_FILEOP_FAILED,
        FilterType.Canceled => Strings.Resources.S_FILEOP_CANCELED,
        FilterType.Previous => Strings.Resources.S_FILEOP_PREVIOUS,
        _ => throw new NotSupportedException()
    };

    public string Name => GetFilterName(Type);

    public FilterType Type { get; }

    public bool IsEnabled => !IsChecked || FileOpFilters.CheckedFilterCount > 1;

    public FileOpFilter(FilterType type)
    {
        Type = type;

        IsChecked = Data.Settings.FileOpFilters.Contains(type);

        FileOpFilters.CheckedFilterCount.PropertyChanged += (sender, e) => OnPropertyChanged(nameof(IsEnabled));
    }
}
