namespace ADB_Explorer.Views.Pages;

public partial class OperationsPageContent : UserControl
{
    private OperationsViewModel ViewModel => (OperationsViewModel)DataContext;

    public OperationsPageContent()
    {
        Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;

        InitializeComponent();

        Loaded += OperationsPageContent_Loaded;
    }

    private void OperationsPageContent_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LinkColumns(
            OpTypeColumn, FileNameColumn, ProgressColumn,
            SourceColumn, DestColumn, TimeStampColumn, DeviceColumn);
    }

    private void DetailedFileOpDataGrid_ColumnDisplayIndexChanged(object sender, DataGridColumnEventArgs e)
    {
        ViewModel.UpdateColumnIndexes();
        QueueTreeRowsAlignment();
    }

    private void DetailedFileOpDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SelectedFileOps = DetailedFileOpDataGrid.SelectedItems.OfType<FileOperation>();

    private void ColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is DataGridColumnHeader header && header.Column is not null && e.NewSize.Width > 0)
            ViewModel.UpdateColumnWidth(header.Column, e.NewSize.Width);

        // Also on a column hiding (width 0), which shifts the ones after it
        QueueTreeRowsAlignment();
    }

    // Rows of the operations' detail trees, whose status column follows the Progress column
    private readonly HashSet<Grid> _treeRows = [];
    private bool _isAlignmentQueued;

    private void FileOpTreeRow_Loaded(object sender, RoutedEventArgs e)
    {
        var row = (Grid)sender;
        _treeRows.Add(row);

        // After the layout pass, when the cells have their final positions
        Dispatcher.InvokeAsync(() => AlignToProgressColumn(row), DispatcherPriority.Loaded);
    }

    private void FileOpTreeRow_Unloaded(object sender, RoutedEventArgs e)
        => _treeRows.Remove((Grid)sender);

    private void QueueTreeRowsAlignment()
    {
        if (_isAlignmentQueued || _treeRows.Count == 0)
            return;

        _isAlignmentQueued = true;
        Dispatcher.InvokeAsync(() =>
        {
            _isAlignmentQueued = false;
            _treeRows.ForEach(AlignToProgressColumn);
        }, DispatcherPriority.Loaded);
    }

    /// <summary>Sizes the name column of a detail tree row so its status starts where the operation's Progress cell does.</summary>
    private void AlignToProgressColumn(Grid treeRow)
    {
        var nameColumn = treeRow.ColumnDefinitions[0];
        var statusColumn = treeRow.ColumnDefinitions[1];

        if (!treeRow.IsLoaded
            || DataGridRow.GetRowContainingElement(treeRow) is not { } row
            || ProgressColumn.Visibility is not Visibility.Visible
            || HitTestHelper.FindAncestor<DataGridCell>(ProgressColumn.GetCellContent(row)) is not { ActualWidth: > 0 } cell)
        {
            nameColumn.Width = new GridLength(150);
            statusColumn.Width = new GridLength(1, GridUnitType.Star);
            return;
        }

        var cellLeft = cell.TransformToAncestor(row).Transform(default).X;
        var treeRowLeft = treeRow.TransformToAncestor(row).Transform(default).X;

        nameColumn.Width = new GridLength(Math.Max(0, cellLeft - treeRowLeft));
        statusColumn.Width = new GridLength(cell.ActualWidth);
    }

    private void DetailedFileOpDataGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        DetailedFileOpDataGrid.UnselectAll();
    }

    private void FileOpRow_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not DataGridRow { DataContext: FileOperation { StatusInfo: FailedOpProgressViewModel } })
            e.Handled = true;
    }

    private void CopyFailedOpError_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Parent: ContextMenu { PlacementTarget: FrameworkElement { DataContext: FileOperation op } } })
            return;

        if (op.StatusInfo is not FailedOpProgressViewModel { Error: { Length: > 0 } error })
            return;

        Clipboard.SetText(error);
    }
}
