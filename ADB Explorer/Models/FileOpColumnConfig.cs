using ADB_Explorer.Helpers;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public class FileOpColumnConfig : ViewModelBase
{
    public enum ColumnType
    {
        OpType,
        FileName,
        Progress,
        Source,
        Dest,
        TimeStamp,
        Device,
    }

    /// <summary>
    /// Shared count of visible columns — used to drive <see cref="IsEnabled"/>.
    /// Updated by <see cref="ViewModels.Pages.OperationsViewModel.UpdateCheckedColumns"/>.
    /// </summary>
    public static ObservableProperty<int> CheckedColumnsCount { get; } = new();

    #region Full properties

    private bool? isChecked;
    public bool? IsChecked
    {
        get => isChecked;
        set
        {
            if (Set(ref isChecked, value))
            {
                if (column is not null)
                    column.Visibility = VisibilityHelper.Visible(value);
            }
        }
    }

    private int index;
    public int Index
    {
        get => index;
        set
        {
            if (Set(ref index, value))
            {
                if (column is not null && value >= 0)
                    column.DisplayIndex = value;
            }
        }
    }

    private double columnWidth;
    public double ColumnWidth
    {
        get => columnWidth;
        set => Set(ref columnWidth, value);
    }

    #endregion

    #region Read-only properties

    public ColumnType Type { get; }

    public string Name => TypeString(Type);

    public bool IsEnabled => IsChecked is false || CheckedColumnsCount > 1;

    public DataGridColumn Column => column;

    #endregion

    private DataGridColumn column;

    public void AssignColumn(DataGridColumn col)
    {
        column = col;
        col.Visibility = VisibilityHelper.Visible(IsChecked);

        if (ColumnWidth > 0)
            col.Width = ColumnWidth;

        if (index >= 0)
            col.DisplayIndex = index;
    }

    public FileOpColumnConfig(ColumnType type, int defaultIndex, double defaultWidth = 0, bool visibleByDefault = true, double? constWidth = null)
    {
        Type = type;

        bool isChecked = visibleByDefault;
        int idx = defaultIndex;
        double width = defaultWidth;

        if (Retrieve() is FileOpColumnState saved)
        {
            isChecked = saved.IsChecked ?? visibleByDefault;
            idx = saved.Index >= 0 ? saved.Index : defaultIndex;
            // Width is not restored for const-width columns
            if (constWidth is null && saved.Width > 0)
                width = saved.Width;
        }

        this.isChecked = isChecked;
        index = idx;
        columnWidth = constWidth ?? width;

        CheckedColumnsCount.PropertyChanged += (_, _) => OnPropertyChanged(nameof(IsEnabled));
    }

    private FileOpColumnState? Retrieve()
    {
        var arr = Data.Settings.FileOpColumns;
        if (arr is null) return null;
        var i = Array.FindIndex(arr, s => s.Type == Type);
        return i >= 0 ? arr[i] : null;
    }

    public static string TypeString(ColumnType columnType) => columnType switch
    {
        ColumnType.OpType => Strings.Resources.S_COLUMN_OP_TYPE,
        ColumnType.FileName => Strings.Resources.S_COLUMN_FILE_NAME,
        ColumnType.Progress => Strings.Resources.S_COLUMN_PROGRESS,
        ColumnType.Source => Strings.Resources.S_COLUMN_SOURCE,
        ColumnType.Dest => Strings.Resources.S_COLUMN_DESTINATION,
        ColumnType.TimeStamp => Strings.Resources.S_COLUMN_ADDED,
        ColumnType.Device => Strings.Resources.S_SETTINGS_GROUP_DEVICE,
        _ => throw new NotSupportedException(),
    };
}

public record struct FileOpColumnState(
    FileOpColumnConfig.ColumnType Type,
    bool? IsChecked,
    int Index,
    double Width);
