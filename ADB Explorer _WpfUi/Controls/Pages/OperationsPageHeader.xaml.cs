using ADB_Explorer.Models;
using ADB_Explorer.ViewModels.Pages;

namespace ADB_Explorer.Controls.Pages;

public partial class OperationsPageHeader : UserControl
{
    private OperationsViewModel ViewModel => (OperationsViewModel)DataContext;

    public OperationsPageHeader()
    {
        Thread.CurrentThread.CurrentCulture =
        Thread.CurrentThread.CurrentUICulture = Data.Settings.UICulture;

        InitializeComponent();

        Loaded += OperationsPageHeader_Loaded;
    }

    private void OperationsPageHeader_Loaded(object sender, RoutedEventArgs e)
    {
        ViewModel.LinkColumns(
            OpTypeColumn, FileNameColumn, ProgressColumn,
            SourceColumn, DestColumn, TimeStampColumn, DeviceColumn);
    }

    private void DetailedFileOpDataGrid_ColumnDisplayIndexChanged(object sender, DataGridColumnEventArgs e)
        => ViewModel.UpdateColumnIndexes();

    private void ColumnHeader_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (sender is DataGridColumnHeader header && e.NewSize.Width > 0)
            ViewModel.UpdateColumnWidth(header.Column, e.NewSize.Width);
    }
}
