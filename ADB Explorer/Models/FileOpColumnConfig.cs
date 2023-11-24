﻿using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

public static class FileOpColumns
{
    public static void Init()
    {
        List<FileOpColumnConfig> tempList = new()
        {
            new(FileOpColumnConfig.ColumnType.OpType,
                (DataTemplate)App.Current.FindResource("OperationIconTemplate"),
                0,
                30,
                icon: "\uE8AF"),
            new(FileOpColumnConfig.ColumnType.FileName,
                (DataTemplate)App.Current.FindResource("FileOpFileNameTemplate"),
                1,
                250),
            new(FileOpColumnConfig.ColumnType.Progress,
                (DataTemplate)App.Current.FindResource("FileOpProgressTemplate"),
                2,
                180),
            new(FileOpColumnConfig.ColumnType.Source,
                (DataTemplate)App.Current.FindResource("FileOpSourcePathTemplate"),
                3,
                200,
                sortPath: nameof(FileOperation.SourcePathString)),
            new(FileOpColumnConfig.ColumnType.Dest,
                (DataTemplate)App.Current.FindResource("FileOpTargetPathTemplate"),
                4,
                200,
                sortPath: nameof(FileOperation.TargetPathString)),
            new(FileOpColumnConfig.ColumnType.TimeStamp,
                (DataTemplate)App.Current.FindResource("FileOpTimeStampTemplate"),
                5,
                70,
                sortPath: $"{nameof(FileOperation.StatusInfo)}.{nameof(FileOperation.StatusInfo.Time)}"),
            new(FileOpColumnConfig.ColumnType.Device,
                (DataTemplate)App.Current.FindResource("FileOpDeviceColumnStyle"),
                6,
                100,
                visibleByDefault: false,
                sortPath: $"{nameof(FileOperation.Device)}.{nameof(FileOperation.Device.Device)}.{nameof(FileOperation.Device.Device.Name)}"),
        };

        List = tempList.OrderBy(c => c.Index).ToList();

        UpdateCheckedColumns();
    }

    public static void UpdateCheckedColumns() =>
        CheckedColumnsCount.Value = List.Count(col => col.IsChecked is true);

    public static void UpdateColumnIndexes() =>
        List.ForEach(c => c.Index = c.Column.DisplayIndex);

    public static List<FileOpColumnConfig> List { get; private set; } = new();

    public static ObservableProperty<int> CheckedColumnsCount { get; set; } = new();
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
        TimeStamp,
        Device,
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

    private double columnWidth;
    public double ColumnWidth
    {
        get => columnWidth;
        set
        {
            if (Set(ref columnWidth, value))
            {
                if (Column is not null)
                    Column.Width = value;

                Store();
            }
        }
    }

    #endregion

    #region Read-only properties

    private object header = null;
    public object Header
    {
        get
        {
            if (header is null)
            {
                header = string.IsNullOrEmpty(Icon)
                    ? Name
                    : new FontIcon()
                    {
                        Glyph = Icon,
                        ToolTip = Name,
                    };
            }

            return header;
        }
    }

    private Style headerStyle = null;
    public Style HeaderStyle
    {
        get
        {
            if (headerStyle is null)
            {
                void FileOpColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
                    => ColumnWidth = e.NewSize.Width;

                headerStyle = new()
                {
                    TargetType = typeof(DataGridColumnHeader),
                    BasedOn = App.Current.FindResource("FileOpColumnHeaderStyle") as Style
                };

                headerStyle.Setters.Add(new EventSetter(FrameworkElement.SizeChangedEvent, new SizeChangedEventHandler(FileOpColumnHeader_SizeChanged)));
            }
            
            return headerStyle;
        }
    }

    private DataGridColumn column = null;
    public DataGridColumn Column
    {
        get
        {
            if (column is null)
            {
                column = new DataGridTemplateColumn()
                {
                    Header = Header,
                    CellTemplate = CellTemplate,
                    HeaderStyle = HeaderStyle,
                    CanUserResize = !string.IsNullOrEmpty(Icon),
                    SortMemberPath = SortPath,
                    Visibility = VisibilityHelper.Visible(IsChecked),
                    Width = ColumnWidth,
                    DisplayIndex = Index,
                };
            }

            return column;
        }
    }

    public DataTemplate CellTemplate { get; }

    public string Icon { get; }

    public ColumnType Type { get; }

    public string SortPath { get; }

    public string Name => TypeString(Type);

    public bool IsEnabled => IsChecked is false || FileOpColumns.CheckedColumnsCount > 1;

    #endregion

    public FileOpColumnConfig(ColumnType type, DataTemplate cellTemplate, int defaultIndex, double defaultWidth = 0, string icon = null, bool visibleByDefault = true, string sortPath = "")
    {
        Type = type;
        Icon = icon;
        CellTemplate = cellTemplate;
        SortPath = sortPath;
        
        bool isChecked = visibleByDefault;
        int index = defaultIndex;
        double width = defaultWidth;

        if (Retrieve() is string storage && storage.Count(c => c == ',') == 2)
        {
            var split = storage.Split(',');
            if (!bool.TryParse(split[0], out isChecked))
                isChecked = visibleByDefault;

            if (!int.TryParse(split[1], out index))
                index = -1;

            if (!double.TryParse(split[2], out width))
                width = defaultWidth;
        }

        IsChecked = isChecked;
        Index = index;
        ColumnWidth = width;

        FileOpColumns.CheckedColumnsCount.PropertyChanged += (object sender, PropertyChangedEventArgs<int> e) => OnPropertyChanged(nameof(IsEnabled));
    }

    public static string TypeString(ColumnType columnType) => columnType switch
    {
        ColumnType.OpType => "Operation Type",
        ColumnType.FileName => "File Name",
        ColumnType.Progress => "Progress",
        ColumnType.Source => "Source",
        ColumnType.Dest => "Destination",
        ColumnType.TimeStamp => "Added",
        ColumnType.Device => "Device",
        _ => throw new NotSupportedException(),
    };

    private string Retrieve() => Storage.RetrieveValue(Type.ToString())?.ToString();

    private void Store() => Storage.StoreValue(Type.ToString(), $"{IsChecked},{Index},{ColumnWidth}");
}
