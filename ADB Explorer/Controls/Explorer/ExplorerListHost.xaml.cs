using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

/// <summary>
/// Hosts the overlapping Explorer list views (grid, icon, and drive views) together with the
/// controls that interact with them directly (<see cref="SelectionRect"/>, the rename tooltip
/// trigger, and the empty-folder placeholder). Split out of <see cref="ExplorerPageContent"/> so
/// that other views (e.g. an upcoming alternate layout) can host the same navigation/details
/// chrome without duplicating list-interaction logic. Cross-boundary calls back into the owning
/// <see cref="ExplorerPageContent"/> go through <see cref="Owner"/>.
/// </summary>
public partial class ExplorerListHost : UserControl
{
    /// <summary>
    /// The <see cref="ExplorerPageContent"/> that hosts this control. Set once via
    /// <see cref="Initialize"/> right after this control is constructed.
    /// </summary>
    internal ExplorerPageContent Owner { get; private set; }

    private ExplorerViewModel ViewModel => (ExplorerViewModel)DataContext;

    /// <summary>
    /// Exposes the drive list view to <see cref="ExplorerPageContent"/>, which no longer has a
    /// named reference to it now that it lives in this control's XAML.
    /// </summary>
    internal Wpf.Ui.Controls.ListView DriveListView => DriveList;

    public ExplorerListHost()
    {
        InitializeComponent();

        PreviewMouseUp += ExplorerListHost_PreviewMouseUp;
        SelectionTimer.Tick += SelectionTimer_Tick;
        _apkPriorityTimer.Tick += (_, _) =>
        {
            _apkPriorityTimer.Stop();
            UpdateApkIconPriorities();
        };

        // Side pane open/close/resize changes column width; icon wrap reflow can push the
        // selection off-screen. Scroll it back after the layout pass settles.
        IconView.SizeChanged += IconView_SizeChanged;
        DriveList.SizeChanged += DriveList_SizeChanged;
        DriveList.SelectionChanged += DriveList_SelectionChanged;
        ContentGrid.SizeChanged += ContentGrid_SizeChanged;

        FileIconView.RenameStarted += IconView_RenameStarted;
        FileIconView.RenameEnded += (_, _) => ClearRename();
    }

    internal void Initialize(ExplorerPageContent owner)
    {
        Owner = owner;

        // Declared locally (not reused from ExplorerPageContent's own) - StaticResource lookup
        // can't reach a parent UserControl's Resources from a different XAML file, since a child
        // resolves its own resources during its own InitializeComponent, before it's attached
        // as that parent's logical child.
        ((BindingProxy)Resources["InstanceProxy"]).Data = owner.Instance;

        owner.Instance.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(ExplorerInstance.IsIconView) or nameof(ExplorerInstance.IsContentView))
                CatchUpActiveViewSelection();
        };
    }

    /// <summary>
    /// Returns the currently active items view (<see cref="IconView"/>, <see cref="ExplorerGrid"/>,
    /// or <see cref="ContentGrid"/>).
    /// </summary>
    internal Selector ActiveView => Owner.Instance.IsIconView
        ? IconView
        : Owner.Instance.IsContentView ? ContentGrid : ExplorerGrid;

    /// <summary>
    /// <see cref="ActiveView"/> cast to <see cref="DataGrid"/>. Only valid when
    /// <see cref="ExplorerInstance.IsIconView"/> is <see langword="false"/> (i.e. the active view
    /// is <see cref="ExplorerGrid"/> or <see cref="ContentGrid"/>, both of which are DataGrids).
    /// </summary>
    private DataGrid ActiveDataGrid => (DataGrid)ActiveView;

    /// <summary>
    /// Returns the selected items from the currently active view.
    /// </summary>
    internal System.Collections.IList ActiveSelectedItems => Owner.Instance.IsIconView
        ? IconView.SelectedItems
        : Owner.Instance.IsContentView ? ContentGrid.SelectedItems : ExplorerGrid.SelectedItems;

    private void ExplorerGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
        Owner.Instance.IsMenuOpen = false;
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // ContentGrid has no headers, so the "clicked the header row" check only applies to the
        // classic (Details) grid; Content view is otherwise treated like Icon view here.
        if (!Owner.Instance.IsIconView && !Owner.Instance.IsContentView)
        {
            var point = Mouse.GetPosition(ExplorerGrid);
            if (point.Y < ColumnHeaderHeight || CopyPaste.WasDragging)
            {
                Owner.Instance.IsMenuOpen = false;
                e.Handled = true;
                ClearWasDraggingAfterContext();
                return;
            }
        }
        else if (CopyPaste.WasDragging)
        {
            Owner.Instance.IsMenuOpen = false;
            e.Handled = true;
            ClearWasDraggingAfterContext();
            return;
        }

        Owner.Instance.IsMenuOpen = true;
        FileActionLogic.UpdateFileActions();
        ExplorerContextMenu.UpdateSeparators();

        if (e.Source is FrameworkElement target)
            target.ContextMenu = CreateRowContextMenu();
    }

    private AdbContextMenu CreateRowContextMenu() => new()
    {
        Style = TryFindResource("ExplorerContextMenuStyle") as Style,
    };

    private void ExplorerGrid_Sorting(object sender, DataGridSortingEventArgs e)
    {
        SortingSelector.SortingProperty sortedColumn;
        if (FileActions.IsAppDrive)
        {
            sortedColumn = e.Column switch
            {
                var c when c == PackageType => SortingSelector.SortingProperty.Type,
                var c when c == PackageUid => SortingSelector.SortingProperty.UserId,
                var c when c == PackageVersion => SortingSelector.SortingProperty.Version,
                _ => SortingSelector.SortingProperty.Name,
            };
        }
        else
        {
            sortedColumn = e.Column switch
            {
                var c when c == DateColumn => SortingSelector.SortingProperty.Date,
                var c when c == TypeColumn => SortingSelector.SortingProperty.Type,
                var c when c == SizeColumn => SortingSelector.SortingProperty.Size,
                _ => SortingSelector.SortingProperty.Name,
            };
        }

        var currentDirection = sortedColumn == Owner.Instance.SortedColumn ? Owner.Instance.SortDirection : null;
        var direction = ListHelper.Invert(currentDirection);
        ViewModel.SetSort(sortedColumn, direction);

        e.Column.SortDirection = direction;
        e.Handled = true;
    }

    private void EmptyNonRootTextBlock_Loaded(object sender, RoutedEventArgs e) => TextHelper.BuildLocalizedInlines(sender, e);

    private void PackageInfoCell_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: Package package } && Owner.Instance.EffectiveDevice is { } device)
            _ = AdbHelper.RequestPackageInfoAsync(device, package, Data.DeviceCts.Token);
    }

    private void DriveList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RuntimeSettings.SelectedDrive = DriveList.SelectedItem as DriveViewModel;
        Owner.DetailsPaneControl.SelectedFiles = RuntimeSettings.SelectedDrive is DriveViewModel selectedDrive ? [selectedDrive] : [];
        FileActionLogic.UpdateFileActions();
    }

    private void DriveList_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var hitItem = e.OriginalSource is DependencyObject source
            ? ItemsControl.ContainerFromElement(DriveList, source)
            : null;

        if (hitItem is not null)
            return;

        foreach (var item in DriveList.Items)
        {
            (item as DriveViewModel)?.IsSelected = false;
        }

        DriveList.SelectedIndex = -1;
        RuntimeSettings.SelectedDrive = null;
        FileActionLogic.UpdateFileActions();
    }
}
