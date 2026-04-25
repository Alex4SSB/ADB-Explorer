using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using Wpf.Ui.Abstractions.Controls;

namespace ADB_Explorer.ViewModels.Pages;

public partial class OperationsViewModel : ObservableObject, INavigationAware
{
    #region File op controls

    public List<FileOpFilter> FilterList => FileOpFilters.FilterList;

    #endregion

    #region Operations

    public ICollectionView Operations { get; }

    #endregion

    #region Column management

    public List<FileOpColumnConfig> ColumnList { get; private set; }

    public FileOpColumnConfig OpTypeConfig { get; private set; }
    public FileOpColumnConfig FileNameConfig { get; private set; }
    public FileOpColumnConfig ProgressConfig { get; private set; }
    public FileOpColumnConfig SourceConfig { get; private set; }
    public FileOpColumnConfig DestConfig { get; private set; }
    public FileOpColumnConfig TimeStampConfig { get; private set; }
    public FileOpColumnConfig DeviceConfig { get; private set; }

    private void InitColumns()
    {
        OpTypeConfig    = new(FileOpColumnConfig.ColumnType.OpType,    defaultIndex: 0, constWidth:   50,  visibleByDefault: true);
        FileNameConfig  = new(FileOpColumnConfig.ColumnType.FileName,  defaultIndex: 1, defaultWidth: 250, visibleByDefault: true);
        ProgressConfig  = new(FileOpColumnConfig.ColumnType.Progress,  defaultIndex: 2, defaultWidth: 180, visibleByDefault: true);
        SourceConfig    = new(FileOpColumnConfig.ColumnType.Source,    defaultIndex: 3, defaultWidth: 200, visibleByDefault: true);
        DestConfig      = new(FileOpColumnConfig.ColumnType.Dest,      defaultIndex: 4, defaultWidth: 200, visibleByDefault: true);
        TimeStampConfig = new(FileOpColumnConfig.ColumnType.TimeStamp, defaultIndex: 5, defaultWidth: 70,  visibleByDefault: true);
        DeviceConfig    = new(FileOpColumnConfig.ColumnType.Device,    defaultIndex: 6, defaultWidth: 100, visibleByDefault: false);

        ColumnList = [OpTypeConfig, FileNameConfig, ProgressConfig, SourceConfig, DestConfig, TimeStampConfig, DeviceConfig];

        foreach (var config in ColumnList)
            config.PropertyChanged += (_, e) => { if (e.PropertyName is nameof(FileOpColumnConfig.IsChecked)) UpdateCheckedColumns(); };

        UpdateCheckedColumns();
    }

    /// <summary>
    /// Called on every <c>Loaded</c> of <c>OperationsPageHeader</c>. Links each XAML
    /// <see cref="DataGridColumn"/> to its config and restores the saved display order.
    /// Safe to call on every navigation — configs are created only once.
    /// </summary>
    public void LinkColumns(
        DataGridColumn opType,
        DataGridColumn fileName,
        DataGridColumn progress,
        DataGridColumn source,
        DataGridColumn dest,
        DataGridColumn timeStamp,
        DataGridColumn device)
    {
        OpTypeConfig.AssignColumn(opType);
        FileNameConfig.AssignColumn(fileName);
        ProgressConfig.AssignColumn(progress);
        SourceConfig.AssignColumn(source);
        DestConfig.AssignColumn(dest);
        TimeStampConfig.AssignColumn(timeStamp);
        DeviceConfig.AssignColumn(device);

        // Apply stored display indices in ascending order to avoid transient conflicts
        foreach (var config in ColumnList.OrderBy(c => c.Index))
        {
            if (config.Column is not null && config.Index >= 0)
                config.Column.DisplayIndex = config.Index;
        }
    }

    public void UpdateCheckedColumns() =>
        FileOpColumnConfig.CheckedColumnsCount.Value = ColumnList.Count(c => c.IsChecked is true);

    public void UpdateColumnIndexes() =>
        ColumnList.ForEach(c => c.Index = c.Column?.DisplayIndex ?? c.Index);

    public void UpdateColumnWidth(DataGridColumn column, double width)
    {
        var config = ColumnList.FirstOrDefault(c => c.Column == column);
        config?.ColumnWidth = width;
    }

    public void StoreColumns() =>
        Data.Settings.FileOpColumns = [.. ColumnList.Select(c => new FileOpColumnState(c.Type, c.IsChecked, c.Index, c.ColumnWidth))];

    #endregion

    #region Selected file ops

    private IEnumerable<FileOperation> selectedFileOps = [];
    public IEnumerable<FileOperation> SelectedFileOps
    {
        get => selectedFileOps;
        set
        {
            if (SetProperty(ref selectedFileOps, value))
            {
                UpdateTooltips();
                ValidateOpAction.NotifyIsEnabledChanged();
            }
        }
    }

    #endregion

    public BaseAction RemoveOpAction { get; private set; }

    public BaseAction ValidateOpAction { get; private set; }

    public BaseAction AddTestOpAction { get; private set; }

    [ObservableProperty]
    public partial string RemoveTooltip { get; private set; }

    [ObservableProperty]
    public partial string ValidateTooltip { get; private set; }

    public OperationsViewModel()
    {
        RemoveOpAction = new(() => Data.FileOpQ.Operations.Count > 0, () =>
        {
            if (SelectedFileOps.Any())
                Data.FileOpQ.Operations.RemoveAll(SelectedFileOps);
            else
                Data.FileOpQ.Operations.Clear();
        });

        ValidateOpAction = new(() => SelectedFileOps.AnyAll(op => op.ValidationAllowed), () =>
        {
            foreach (var item in SelectedFileOps)
            {
                Security.ValidateOperation(item);
            }
        });

        AddTestOpAction = new(() => true, () =>
        {
            var percentage = Random.Shared.Next(1, 30);
            var op = FileSyncOperation.CreateTestPullOp(Data.DevicesObject.Current, percentage);
            Data.FileOpQ.Operations.Add(op);
        });

        Operations = new CollectionViewSource { Source = Data.FileOpQ.Operations }.View;
        Operations.Filter = op => op is FileOperation fileOp && Data.Settings.FileOpFilters.Contains(fileOp.Filter);
        
        if (Operations is ICollectionViewLiveShaping liveShaping)
        {
            liveShaping.LiveFilteringProperties.Add(nameof(FileOperation.Status));
            liveShaping.LiveFilteringProperties.Add(nameof(FileOperation.IsValidated));
            liveShaping.LiveFilteringProperties.Add(nameof(FileOperation.IsPastOp));
            liveShaping.IsLiveFiltering = true;
        }

        FileOpFilters.CheckedFilterCount.PropertyChanged += (_, _) => Operations.Refresh();

        InitColumns();

        Data.FileOpQ.Operations.CollectionChanged += (_, _) =>
        {
            UpdateTooltips();
        };
    }

    public void UpdateTooltips()
    {
        var plural = SelectedFileOps.Count() != 1;
        var opString = plural
            ? Strings.Resources.S_ACTION_OPERATION_PLURAL
            : Strings.Resources.S_ACTION_OPERATION;

        RemoveTooltip = string.Format(Strings.Resources.S_REM_DEVICE_TITLE, opString);
        ValidateTooltip = string.Format(Strings.Resources.S_ACTION_VALIDATE, opString);
    }

    public Task OnNavigatedToAsync() => Task.CompletedTask;

    public Task OnNavigatedFromAsync() => Task.CompletedTask;
}
