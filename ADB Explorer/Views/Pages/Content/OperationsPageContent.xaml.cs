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
        => ViewModel.UpdateColumnIndexes();

    private void DetailedFileOpDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => ViewModel.SelectedFileOps = DetailedFileOpDataGrid.SelectedItems.OfType<FileOperation>();

    private void ColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is DataGridColumnHeader header && header.Column is not null && e.NewSize.Width > 0)
            ViewModel.UpdateColumnWidth(header.Column, e.NewSize.Width);
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
