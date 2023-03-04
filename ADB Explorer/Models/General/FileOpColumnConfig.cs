using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public static class FileOpColumns
{
    public static void Init()
    {
        CheckedColumnsCount = new();

        List = new()
        {
            new(FileOpColumnConfig.ColumnType.OpType, "Operation Type", 30, "\uE8AF"),
            new(FileOpColumnConfig.ColumnType.FileName, "File Name", 250),
            new(FileOpColumnConfig.ColumnType.Progress, "Progress", 180),
            new(FileOpColumnConfig.ColumnType.Source, "Source", 200),
            new(FileOpColumnConfig.ColumnType.Dest, "Destination", 200),
        };

        UpdateCheckedColumns();
    }

    public static void UpdateCheckedColumns() =>
        CheckedColumnsCount.Value = List.Count(col => col.IsChecked is true);

    public static List<FileOpColumnConfig> List { get; private set; } = new();

    public static ObservableProperty<int> CheckedColumnsCount { get; set; }
}

public class FileOpColumnConfig : ViewModelBase
{
    public enum ColumnType
    {
        OpType,
        FileName,
        Progress,
        Source,
        Dest,
    }

    #region Full properties

    private bool? isChecked;
    public bool? IsChecked
    {
        get => isChecked;
        set
        {
            if (Set(ref isChecked, value))
            {
                if (Column is not null)
                    Column.Visibility = VisibilityHelper.Visible(value);

                FileOpColumns.UpdateCheckedColumns();

                Store();
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
                if (Column is not null && value >= 0)
                    Column.DisplayIndex = value;

                Store();
            }
        }
    }

    private double width;
    public double Width
    {
        get => width;
        set
        {
            if (Set(ref width, value))
            {
                if (Column is not null)
                    Column.Width = value;

                Store();
            }
        }
    }

    private DataGridColumn column;
    public DataGridColumn Column
    {
        get => column;
        set
        {
            column = value;

            Column.Visibility = VisibilityHelper.Visible(IsChecked);
            Column.Width = Width;
            if (Index >= 0)
                Column.DisplayIndex = Index;
        }
    }

    #endregion

    #region Read-only properties

    public string Name { get; }

    public string Icon { get; }

    public ColumnType Type { get; }

    public bool IsEnabled => IsChecked is false || FileOpColumns.CheckedColumnsCount > 1;

    #endregion

    public FileOpColumnConfig(ColumnType type, string name, double defaultWidth = 0, string icon = null)
    {
        Type = type;
        Name = name;
        Icon = icon;

        if (Retrieve() is string storage && storage.Count(c => c == ',') == 2)
        {
            var split = storage.Split(',');
            IsChecked = !bool.TryParse(split[0], out bool isChecked) || isChecked; // default is true
            Index = int.TryParse(split[1], out int index) ? index : -1;
            Width = double.TryParse(split[2], out double width) ? width : defaultWidth;
        }

        FileOpColumns.CheckedColumnsCount.PropertyChanged += (object sender, PropertyChangedEventArgs<int> e) => OnPropertyChanged(nameof(IsEnabled));
    }

    private string Retrieve() => Storage.RetrieveValue(Type.ToString())?.ToString();

    private void Store() => Storage.StoreValue(Type.ToString(), $"{IsChecked},{Index},{Width}");
}
