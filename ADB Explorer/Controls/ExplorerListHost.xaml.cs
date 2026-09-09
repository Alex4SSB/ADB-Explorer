using ADB_Explorer.Controls.Pages;
using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using ADB_Explorer.Views;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.FileAction;

namespace ADB_Explorer.Controls;

/// <summary>
/// Hosts the overlapping Explorer list views (grid, icon, and drive views) together with the
/// controls that interact with them directly (<see cref="SelectionRect"/>, the rename tooltip
/// trigger, and the empty-folder placeholder). Split out of <see cref="ExplorerPageHeader"/> so
/// that other views (e.g. an upcoming alternate layout) can host the same navigation/details
/// chrome without duplicating list-interaction logic. Cross-boundary calls back into the owning
/// <see cref="ExplorerPageHeader"/> go through <see cref="Owner"/>.
/// </summary>
public partial class ExplorerListHost : UserControl
{
    /// <summary>
    /// The <see cref="ExplorerPageHeader"/> that hosts this control. Set once via
    /// <see cref="Initialize"/> right after this control is constructed.
    /// </summary>
    internal ExplorerPageHeader Owner { get; private set; }

    private ExplorerViewModel ViewModel => (ExplorerViewModel)DataContext;

    /// <summary>
    /// Exposes the drive list view to <see cref="ExplorerPageHeader"/>, which no longer has a
    /// named reference to it now that it lives in this control's XAML.
    /// </summary>
    internal Wpf.Ui.Controls.ListView DriveListView => DriveList;

    public ExplorerListHost()
    {
        InitializeComponent();

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

    /// <summary>
    /// Links this control back to its owning <see cref="ExplorerPageHeader"/>. Must be called
    /// once, immediately after construction, before any user interaction can reach this control.
    /// </summary>
    internal void Initialize(ExplorerPageHeader owner)
    {
        Owner = owner;
    }

    /// <summary>
    /// Clears the tracked mouse-down point unless a marquee/drag is in progress. Bridges
    /// <see cref="ExplorerPageHeader"/>'s window-level <c>Grid_MouseEnter</c> handler to this
    /// control's internal mouse-gesture state.
    /// </summary>
    public void ClearMouseDownPointIfIdle()
    {
        if (Mouse.LeftButton is MouseButtonState.Pressed || SelectionRect.IsActive)
            return;

        MouseDownPoint = NullPoint;
    }

    private int ClickCount = 0;

    private bool WasSelected;

    private bool WasEditing;

    private Point MouseDownPoint;

    private TextBox? _renameTextBox;

    /// <summary>
    /// Returns the currently active items view (<see cref="IconView"/>, <see cref="ExplorerGrid"/>,
    /// or <see cref="ContentGrid"/>).
    /// </summary>
    internal Selector ActiveView => ViewModel.IsIconView
        ? IconView
        : ViewModel.IsContentView ? ContentGrid : ExplorerGrid;

    /// <summary>
    /// <see cref="ActiveView"/> cast to <see cref="DataGrid"/>. Only valid when
    /// <see cref="ExplorerViewModel.IsIconView"/> is <see langword="false"/> (i.e. the active view
    /// is <see cref="ExplorerGrid"/> or <see cref="ContentGrid"/>, both of which are DataGrids).
    /// </summary>
    private DataGrid ActiveDataGrid => (DataGrid)ActiveView;

    /// <summary>
    /// Returns the selected items from the currently active view.
    /// </summary>
    internal System.Collections.IList ActiveSelectedItems => ViewModel.IsIconView
        ? IconView.SelectedItems
        : ViewModel.IsContentView ? ContentGrid.SelectedItems : ExplorerGrid.SelectedItems;

    internal void ActiveUnselectAll()
    {
        try
        {
            if (ViewModel.IsIconView)
                IconView.UnselectAll();
            else if (ViewModel.IsContentView)
                ContentGrid.UnselectAll();
            else
                ExplorerGrid.UnselectAll();
        }
        catch
        { }
    }

    private void ActiveSelectAll()
    {
        if (ViewModel.IsIconView)
            IconView.SelectAll();
        else if (ViewModel.IsContentView)
            ContentGrid.SelectAll();
        else
            ExplorerGrid.SelectAll();
    }

    internal void ToggleSelectAll()
    {
        if (ActiveView.Items.Count == ActiveSelectedItems.Count && ActiveSelectedItems.Count > 0)
            ActiveUnselectAll();
        else
            ActiveSelectAll();
    }

    internal void ActiveScrollIntoView(object item)
    {
        if (item is null)
            return;

        if (ViewModel.IsIconView)
            IconView.ScrollIntoView(item);
        else
        {
            ActiveDataGrid.ScrollIntoView(item);
            // ScrollIntoView aligns the row box and skips the 3px left margin; restore it.
            ResetExplorerHorizontalScroll();
        }
    }

    internal ScrollViewer ExplorerScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(ExplorerGrid);
            return field;
        }
    } = null;

    internal ScrollViewer ContentScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(ContentGrid);
            return field;
        }
    } = null;

    internal ScrollViewer IconScrollViewer
    {
        get
        {
            field ??= StyleHelper.FindDescendant<ScrollViewer>(IconView);
            return field;
        }
    } = null;

    internal ScrollViewer ActiveScrollViewer => ViewModel.IsIconView
        ? IconScrollViewer
        : ViewModel.IsContentView ? ContentScrollViewer : ExplorerScrollViewer;

    internal void ResetExplorerHorizontalScroll()
    {
        void reset() => ActiveScrollViewer?.ScrollToHorizontalOffset(0);

        reset();
        // DataGrid.ScrollIntoView often defers BringIntoView to Loaded; run after that
        // so the row left margin is not scrolled off against the tree splitter.
        App.SafeBeginInvoke(reset, DispatcherPriority.Loaded);
        App.SafeBeginInvoke(reset, DispatcherPriority.Input);
    }

    private static Point NullPoint => new(-1, -1);

    private bool IsExplorerNameOrIconHit(DependencyObject? originalSource, Point positionInSelectionRect)
    {
        if (originalSource is null || positionInSelectionRect == NullPoint)
            return false;

        // Content view has separate real columns per field, but the icon and name/path both live
        // in ContentNameColumn's cell template, so the icon and name "columns" are the same column
        // there; there is no package column, and Type/Date-Size are intentionally excluded.
        DataGridColumn? iconColumn = IconColumn;
        DataGridColumn? nameColumn = NameColumn;
        DataGridColumn? packageColumn = PackageName;
        if (ViewModel.IsContentView)
        {
            iconColumn = ContentNameColumn;
            nameColumn = ContentNameColumn;
            packageColumn = null;
        }

        return HitTestHelper.IsExplorerNameOrIconHit(
            originalSource,
            positionInSelectionRect,
            SelectionRect,
            ViewModel.IsIconView,
            iconColumn,
            nameColumn,
            packageColumn);
    }

    private void TrackExplorerMouseDown(MouseButtonEventArgs e, DependencyObject? originalSource, bool itemAlreadySelected)
    {
        SelectionRect.ResetGesture();

        MouseDownPoint = SuppressExplorerMarquee
            ? NullPoint
            : e.GetPosition(SelectionRect);

        if (MouseDownPoint == NullPoint)
        {
            CopyPaste.DragStatus = CopyPasteService.DragState.None;
            return;
        }

        // An already-selected item arms drag from anywhere within its own row/tile (callers pass
        // itemAlreadySelected only when originalSource is actually inside that item's container).
        // An unselected item only arms drag from its own text/icon - mouse down anywhere else
        // starts a rubber-band marquee instead. Applies across all views.
        CopyPaste.DragStatus = itemAlreadySelected || IsExplorerNameOrIconHit(originalSource, MouseDownPoint)
            ? CopyPasteService.DragState.Pending
            : CopyPasteService.DragState.None;
    }

    private void TryBeginExplorerDragOrMarquee(Point point, bool abort, ScrollViewer scroller, DependencyObject? dragSource)
    {
        if (abort || CopyPaste.WasDragging || CopyPaste.DragStatus is CopyPasteService.DragState.Active)
        {
            SelectionRect.Collapse();
            return;
        }

        if (SelectionRect.IsActive)
        {
            SelectionRect.Update(point, MouseDownPoint, scroller, ActiveView, ActiveSelectedItems, ViewModel);
            return;
        }

        if ((MouseDownPoint - point).LengthSquared < DRAG_START_DISTANCE_SQUARED)
            return;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Pending
            && ActiveSelectedItems.Count > 0
            && ActiveSelectedItems[0] is FileClass or Package)
        {
            InitiateDrag(dragSource);
            return;
        }

        CopyPaste.DragStatus = CopyPasteService.DragState.None;
        SelectionRect.Update(point, MouseDownPoint, scroller, ActiveView, ActiveSelectedItems, ViewModel);
    }

    /// <summary>
    /// Skip rubber-band / file drag from this click (open menu, or the click that just closed one).
    /// </summary>
    private bool SuppressExplorerMarquee =>
        ViewModel.IsMenuOpen || Owner.ToolbarSubmenuDepth > 0 || Owner.SuppressSelectionAfterMenu;

    /// <summary>
    /// Skip clearing selection only while the explorer context menu is open.
    /// Toolbar submenu dismiss is handled by <see cref="SuppressExplorerMarquee"/> so
    /// empty-space unselect still runs on that click.
    /// </summary>
    private bool SuppressExplorerUnselect => ViewModel.IsMenuOpen;

    internal void CancelExplorerMarquee()
    {
        MouseDownPoint = NullPoint;
        CopyPaste.DragStatus = CopyPasteService.DragState.None;
        SelectionRect.Collapse();
    }

    private double? RowHeight { get; set; }

    private double ColumnHeaderHeight => (double)FindResource("DataGridColumnHeaderHeight") + ScrollContentPresenterMargin;

    private double ScrollContentPresenterMargin => ((Thickness)FindResource("DataGridScrollContentPresenterMargin")).Top;

    private double DataGridContentWidth
        => StyleHelper.FindDescendant<ItemsPresenter>(ExplorerGrid) is ItemsPresenter presenter ? presenter.ActualWidth : 0;

    internal bool IsInEditMode
    {
        get
        {
            if (FileActions.IsAppDrive)
                return false;

            if (ActiveView.SelectedItem is not FileClass file)
                return false;

            var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            return vm.IsInEditMode;
        }
        set
        {
            if (value && !FileActions.RenameEnabled)
                return;

            if (ActiveView.SelectedItem is not FileClass file)
                return;

            var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
            vm.IsInEditMode = value;
            FileActions.IsExplorerEditing = value;
        }
    }

    private readonly DispatcherTimer SelectionTimer = new() { Interval = SELECTION_CHANGED_DELAY };

    /// <summary>Debounces APK icon priority updates on scroll / selection.</summary>
    private readonly DispatcherTimer _apkPriorityTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };

    /// <summary>
    /// Coalesces wrap-panel reflows (side pane open/close/resize) so we scroll once after layout.
    /// </summary>
    private int _keepSelectionInViewGeneration;

    /// <summary>
    /// Coalesces Content view column layout recalculations - see
    /// <see cref="ContentGrid_SizeChanged"/> for why this is deferred rather than applied inline.
    /// </summary>
    private int _contentColumnsGeneration;

    private bool _isSyncingSelection = false;

    internal bool DriveViewKeyNavigation(Key key)
    {
        if (DriveList.Items.Count == 0)
            return false;

        if (DriveList.SelectedItems.Count == 0)
        {
            switch (key)
            {
                case Key.Left or Key.Up:
                    DriveList.SelectedIndex = DriveList.Items.Count - 1;
                    break;

                case Key.Right or Key.Down:
                    DriveList.SelectedIndex = 0;
                    break;

                default:
                    return false;
            }

            SelectionHelper.GetListViewItemContainer(DriveList).Focus();
            return true;
        }

        switch (key)
        {
            case Key.Enter:
                ((DriveViewModel)DriveList.SelectedItem).BrowseCommand.Execute();
                return true;

            case Key.Escape:
                // Should've been clear selected drives, but causes inconsistent behavior
                return true;

            default:
                return false;
        }
    }

    internal bool ExplorerGridKeyNavigation(Key key)
    {
        if (ActiveView.Items.Count < 1 || Owner.DetailsPaneControl.IsEditorFocused)
            return false;

        switch (key)
        {
            case Key.Escape:
                if (SelectionRect.IsActive)
                    return true;

                ActiveUnselectAll();
                break;

            case Key.Left or Key.Right when !ViewModel.IsIconView:
                return false;

            case Key.Down or Key.Up or Key.Left or Key.Right or Key.Home or Key.End:
                if (Owner.BfNavigation)
                {
                    ViewModel.CurrentSelectedIndex = ActiveView.SelectedIndex;
                    Owner.BfNavigation = false;
                }

                if (ViewModel.IsIconView)
                {
                    var navKey = key;
                    if (RuntimeSettings.IsRTL && navKey is Key.Left or Key.Right)
                        navKey = navKey == Key.Left ? Key.Right : Key.Left;

                    var step = navKey is Key.Left or Key.Right ? 1 : IconView.ItemsPerRow;

                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        IconView.MultiSelect(navKey, step, ViewModel);
                    else
                        IconView.SingleSelect(navKey, step, ViewModel);
                }
                else
                {
                    if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                        ActiveDataGrid.MultiSelect(key, ViewModel);
                    else
                        ActiveDataGrid.SingleSelect(key, ViewModel);
                }
                break;

            case Key.Enter:
                // Shift+Enter is bound to FollowLink on the main window; do not swallow it here.
                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                    return false;

                if (ExplorerGrid.SelectedCells.Count < 1 || IsInEditMode)
                    return false;

                if (ActiveSelectedItems.Count == 1
                    && ActiveView.SelectedItem is FileClass selected
                    && FileActionLogic.CanEnterSelection(selected))
                    DoubleClick(ActiveView.SelectedItem);
                return true;

            case Key.Apps:
                ActiveView.ContextMenu.IsOpen = true;
                break;

            default:
                return false;
        }

        return true;
    }

    private void SelectionTimer_Tick(object? sender, EventArgs e)
    {
        SelectionTimer.Stop();
        ApplySelectionEffects();
    }

    private void ApplySelectionEffects()
    {
        var files = Files;
        files.SelectedFiles = files.Actions.IsAppDrive ? [] : (files.DirList?.FileList?.Where(f => f.IsSelected) ?? []);
        files.SelectedPackages = files.Actions.IsAppDrive
            ? (Packages?.Where(p => p.IsSelected) ?? [])
            : [];
        files.Actions.SelectedItemsCount = files.Actions.IsAppDrive
            ? files.SelectedPackages.Count()
            : files.SelectedFiles.Count();

        if (Owner.DetailsPaneControl.IsOpen)
        {
            // Snapshot so OldValue isn't a live Where() that re-evaluates after selection changes.
            Owner.DetailsPaneControl.SelectedFiles = files.Actions.IsAppDrive
                ? files.SelectedPackages.ToList()
                : files.SelectedFiles.ToList();
        }

        if (DevicesObject.Current is { SupportsLsV2: false })
        {
            foreach (var file in files.SelectedFiles.Where(f => f.IsRegularFile && f.ShellLsSize is null))
            {
                if (Owner.DetailsPaneControl.IsOpen && !file.IsCreationTimeResolved)
                    continue;

                file.UpdateSizeFromShell(CancellationToken.None);
            }
        }

        ViewModel.NotifySelectedFilesTotalSize();

        FileActionLogic.UpdateFileActions(files);

        if (files.Actions.IsAppDrive)
            ScheduleApkIconPriorityUpdate();
    }

    internal void ScheduleApkIconPriorityUpdate()
    {
        _apkPriorityTimer.Stop();
        _apkPriorityTimer.Start();
    }

    private void UpdateApkIconPriorities()
    {
        if (!FileActions.IsAppDrive || Data.Packages is null || Data.Packages.Count == 0)
            return;

        var selected = SelectedPackages.ToList();
        var visible = CollectVisiblePackages();
        ApkIconService.UpdatePackageLoadPriorities(selected, visible);
    }

    private List<Package> CollectVisiblePackages()
    {
        List<Package> visible = [];
        if (ViewModel.IsIconView)
        {
            var range = IconView.VisibleRange;
            var count = IconView.Items.Count;
            for (int i = range.StartIndex; i <= range.EndIndex && i < count; i++)
            {
                if (i < 0)
                    continue;
                if (IconView.Items[i] is Package package)
                    visible.Add(package);
            }
        }
        else
        {
            var grid = ActiveDataGrid;
            var generator = grid.ItemContainerGenerator;
            for (int i = 0; i < grid.Items.Count; i++)
            {
                if (generator.ContainerFromIndex(i) is null)
                    continue;
                if (grid.Items[i] is Package package)
                    visible.Add(package);
            }
        }

        return visible;
    }

    private void ExplorerScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (!FileActions.IsAppDrive)
            return;
        if (e.VerticalChange == 0 && e.ViewportHeightChange == 0 && e.ExtentHeightChange == 0)
            return;

        ScheduleApkIconPriorityUpdate();
    }

    internal void ClearSelectionForSearch()
    {
        ActiveUnselectAll();
        ClearDataItemSelectionFlags();
        FileActions.SelectedItemsCount = 0;
        SelectedFiles = [];
        SelectedPackages = [];
        if (Owner.DetailsPaneControl is not null)
            Owner.DetailsPaneControl.SelectedFiles = [];
    }

    private void DataGridCell_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (e.OriginalSource is DataGridCell && e.TargetRect == Rect.Empty)
        {
            e.Handled = true;
        }
    }

    private void DataGridCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        SelectionRect.ResetGesture();

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        // Left-button only - a right-click that cancels an in-progress drag (standard OLE
        // behavior) surfaces here as this same click's mouse-down, and resetting WasDragging for
        // it would clobber the flag before ExplorerGrid_ContextMenuOpening's suppression check can
        // see it, letting the context menu open right after the cancelled drag.
        if (e.ChangedButton is MouseButton.Left)
            CopyPaste.WasDragging = false;

        var cell = sender as DataGridCell;
        WasEditing = cell.DataContext is FileClass clickedFile && clickedFile.FolderViewModel.IsInEditMode;

        if (WasEditing)
        {
            // A click anywhere else in the row currently being renamed - not on the rename TextBox
            // itself, which handles its own clicks for caret placement / text selection - should
            // behave like clicking away from the item: commit the rename, rather than silently
            // swallowing the click and leaving the TextBox in edit mode.
            if (_renameTextBox is not null && !IsWithinElement(e.OriginalSource as DependencyObject, _renameTextBox))
                FileViewModelBase.RenameCommit(_renameTextBox, ExitFolderEditMode);

            return;
        }

        var row = DataGridRow.GetRowContainingElement(cell);
        var current = row.GetIndex();

        WasSelected = row.IsSelected;

        if (e.ChangedButton is MouseButton.Right && !WasSelected)
        {
            SelectOnlyItem(row.Item);
            e.Handled = true;
            return;
        }

        TrackExplorerMouseDown(e, e.OriginalSource as DependencyObject, WasSelected);
        e.Handled = true;
        ClickCount = e.ClickCount;

        if (ClickCount > 1)
        {
            DoubleClick(cell.DataContext);
            ClickCount = -1;
            return;
        }

        Owner.PathBoxFocus(false);
        RaiseUnfocusSearchBox();

        if (!row.IsSelected
            && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectOnlyItem(row.Item);
        }

        ViewModel.NextSelectedIndex = current;
        ViewModel.CurrentSelectedIndex = current;
        if (ExplorerGrid.SelectedItems.Count < 1)
            ViewModel.FirstSelectedIndex = current;
    }

    /// <summary>
    /// Clears selection on both views and on virtualized <see cref="FilePath.IsSelected"/> flags,
    /// then selects <paramref name="item"/> alone.
    /// </summary>
    private void SelectOnlyItem(object item)
    {
        if (Keyboard.Modifiers is ModifierKeys.Control or ModifierKeys.Shift)
            return;

        ClearDataItemSelectionFlags();
        ActiveUnselectAll();

        if (item is FilePath filePath)
            filePath.IsSelected = true;
        else if (item is Package package)
            package.IsSelected = true;

        ActiveView.SelectedItem = item;
    }

    private void ClearDataItemSelectionFlags()
    {
        if (FileActions.IsAppDrive)
        {
            // Prefer the full package list — ActiveView.Items may omit filtered/system packages
            // while their IsSelected flags still linger from virtualization.
            var packages = Data.Packages ?? ExplorerGrid.Items.OfType<Package>();
            foreach (var pkg in packages)
            {
                if (pkg.IsSelected)
                    pkg.IsSelected = false;
            }
            return;
        }

        if (DirList?.FileList is null)
            return;

        foreach (var file in DirList.FileList)
        {
            if (file.IsSelected)
                file.IsSelected = false;
        }
    }

    private void DoubleClick(object source)
    {
        FileIconView.CancelDelayedRename();

        if (FileActions.IsRecycleBin)
            return;

        if (source is not FileClass file)
        {
            if (source is Package apk && !FileActions.ListingInProgress)
                FileActionLogic.OpenApkLocation(apk);

            return;
        }

        if (file.Type is FileType.Folder)
        {
            if (!FileActions.ListingInProgress)
            {
                Owner.BfNavigation = false;
                Owner.NavigateToPath(file);
            }

            return;
        }
        else if (file.Type is not FileType.File)
            return;

        if (!FileActions.IsAppDrive
            && DevicesObject.Current is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, FileActions.IsArchive))
        {
            if (!FileActions.ListingInProgress)
            {
                Owner.BfNavigation = false;
                Owner.NavigateToPath(file);
            }

            return;
        }

        if (Settings.DoubleClickToPull
            && Settings.IsPullOnDoubleClickEnabled
            && FileActions.PullEnabled)
        {
            FileActionLogic.PullFiles(Settings.DefaultFolder);
        }
    }

    private void DataGridCell_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left || ClickCount < 0)
            return;

        if (SelectionRect.IsActive || SelectionRect.SelectionOccurred)
        {
            SelectionRect.Collapse();
            e.Handled = true;

            return;
        }

        e.Handled = CellMouseUp(sender, e);

        CopyPaste.DragStatus = CopyPasteService.DragState.None;
    }

    private bool CellMouseUp(object sender, MouseButtonEventArgs e)
    {
        DataGridCell cell;
        DataGridRow row;

        if (CopyPaste.DragStatus is CopyPasteService.DragState.Active || CopyPaste.WasDragging)
        {
            CopyPaste.WasDragging = false;
            return true;
        }

        switch (sender)
        {
            case DataGridCell c:
                {
                    cell = c;
                    row = DataGridRow.GetRowContainingElement(cell);

                    if (row is null)
                        return false;

                    if (cell.DataContext is FileClass clickedFile && clickedFile.FolderViewModel.IsInEditMode)
                        return false;
                    break;
                }
            case DataGridRow r:
                row = r;
                cell = null;
                break;
            default:
                return false;
        }

        // Row can be mid-recycling by the time mouse-up fires (e.g. a fast double-click that
        // triggered navigation between down and up), leaving it detached from its DataGrid.
        if (ItemsControl.ItemsControlFromItemContainer(row) is not DataGrid grid)
            return false;

        var current = row.GetIndex();
        ViewModel.CurrentSelectedIndex = current;

        if (MultiRowSelect(row))
            return true;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = current;
        }

        if (!row.IsSelected || grid.SelectedItems?.Count != 1)
        {
            grid.UnselectAll();
            row.IsSelected = true;
            return true;
        }

        if (cell is not null && (cell.Column == NameColumn || cell.Column == ContentNameColumn))
            MouseUpOnName(cell, grid, e.OriginalSource as DependencyObject);

        return true;
    }

    /// <summary>
    /// Content view's single template column holds the icon, name, path, type, and date/size all
    /// in one cell, so a plain "which column was clicked" check (sufficient for the classic grid's
    /// dedicated Name column) would let a click anywhere in the row arm the click-to-rename timer.
    /// Require the click to have actually landed on the name text itself there.
    /// </summary>
    /// <summary>x:Name given (per DataTemplate instance, not a class field) to the Content view's name TextBlock.</summary>
    private const string ContentNameElementName = "ContentNameText";

    private static bool IsContentViewNameTextHit(DependencyObject? originalSource)
    {
        for (var dep = originalSource; dep is not null; dep = HitTestHelper.GetVisualOrLogicalParent(dep))
        {
            if (dep is FrameworkElement { Name: ContentNameElementName })
                return true;
        }

        return false;
    }

    private void MouseUpOnName(DataGridCell cell, DataGrid grid, DependencyObject? originalSource)
    {
        if (grid == ContentGrid && !IsContentViewNameTextHit(originalSource))
            return;

        if (!DevicesObject.Current.HasRootShell
            && ((FileClass)cell.DataContext).Type is not (FileType.File or FileType.Folder))
            return;

        if (!FileActions.RenameEnabled)
            return;

        var file = (FileClass)grid.SelectedItem;
        var path = file.FullPath;

        if (grid.SelectedItems.Count == 1 && WasSelected && !WasEditing)
        {
            Task.Run(() =>
            {
                var start = DateTime.Now;

                while (true)
                {
                    Task.Delay(100);

                    if (DateTime.Now - start > RENAME_CLICK_DELAY)
                        break;

                    var currentPath = App.AppDispatcher?.Invoke(() => ((FileClass)grid.SelectedItem)?.FullPath);
                    if (ClickCount != 1 || currentPath != path)
                        return;
                }

                App.SafeInvoke(() =>
                {
                    if (ClickCount != 1)
                        return;

                    file.FolderViewModel.IsInEditMode = true;
                    FileActions.IsExplorerEditing = true;
                });
            });
        }
    }

    private bool MultiRowSelect(DataGridRow row)
    {
        var grid = (DataGrid)ItemsControl.ItemsControlFromItemContainer(row);
        var current = row.GetIndex();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            grid.UnselectAll();

            var firstSelected = ViewModel.FirstSelectedIndex;
            // FirstSelectedIndex defaults to -1 until a plain click sets it. If Shift+click
            // is the very first selection in a session, treat the clicked row as the start
            // of the range instead of indexing into Items[-1] below.
            if (firstSelected < 0)
                firstSelected = current;

            int firstUnselected = firstSelected, lastUnselected = current + 1;
            if (current < firstSelected)
            {
                firstUnselected = current;
                lastUnselected = firstSelected + 1;
            }

            for (int i = firstUnselected; i < lastUnselected; i++)
            {
                grid.SelectedItems.Add(grid.Items[i]);
            }

            return true;
        }

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control))
        {
            row.IsSelected = !row.IsSelected;
            return true;
        }

        return false;
    }

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        SelectionRect.ResetGesture();

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        CopyPaste.WasDragging = false;
        var row = sender as DataGridRow;

        TrackExplorerMouseDown(e, e.OriginalSource as DependencyObject, row is not null && row.IsSelected);

        ViewModel.SetIndexSingle(row.GetIndex());
    }

    private void ItemContainer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInEditMode || ActiveSelectedItems.Count != 1)
            return;

        ClickCount = -1;
        DoubleClick(ActiveView.SelectedItem);
    }

    private void DataGridRow_KeyDown(object sender, KeyEventArgs e)
    {
        var grid = (DataGrid)ItemsControl.ItemsControlFromItemContainer((DependencyObject)sender);

        var key = e.Key;
        switch (key)
        {
            case Key.Enter when IsInEditMode:
                return;
            case Key.Enter when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                return;
            case Key.Enter:
                {
                    if (grid.SelectedItems.Count == 1
                        && grid.SelectedItem is FileClass selected
                        && FileActionLogic.CanEnterSelection(selected))
                        DoubleClick(grid.SelectedItem);
                    break;
                }
            case Key.Back:
                NavHistory.NavigateBF(Navigation.SpecialLocation.Back);
                break;

            case Key.Delete when FileActions.DeleteEnabled:
                FileActionLogic.DeleteFiles();
                break;

            case Key.Up or Key.Down when Keyboard.Modifiers.HasFlag(ModifierKeys.Shift):
                grid.MultiSelect(key, ViewModel);
                break;

            case Key.Up or Key.Down:
                grid.SingleSelect(key, ViewModel);
                break;

            case Key.F2:
                if (FileActions.RenameEnabled)
                    AppActions.List.First(action => action.Name is FileActionType.Rename).Command.Execute();
                break;

            default:
                return;
        }

        e.Handled = true;
    }

    private void DataGridRow_Drop(object sender, DragEventArgs e)
    {
        CopyPaste.AcceptDataObject(e, (FrameworkElement)sender);
        e.Handled = true;
    }

    private void Row_PreviewDragEnter(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = true;
    }

    private void Row_PreviewDragLeave(object sender, DragEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = false;
    }

    private void Row_MouseLeave(object sender, MouseEventArgs e)
    {
        if (((FrameworkElement)sender).DataContext is FileClass file)
            file.FolderViewModel.IsDragOver = false;
    }

    private void ExplorerGrid_DragOver(object sender, DragEventArgs e)
    {
        var allowed = CopyPaste.GetAllowedDragEffects(e.Data, (FrameworkElement)sender);

        if (allowed.HasFlag(DragDropEffects.Move) && CopyPaste.IsSelf && !e.KeyStates.HasFlag(DragDropKeyStates.ControlKey) && !e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
        {
            e.Effects = DragDropEffects.Move;
        }
        else if (allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey))
        {
            e.Effects = DragDropEffects.Link;
        }
        else if (allowed.HasFlag(DragDropEffects.Copy)) // copy is the default and does not require Ctrl to be activated
        {
            e.Effects = DragDropEffects.Copy;
        }
        else
            e.Effects = allowed;

        if ((!allowed.HasFlag(DragDropEffects.Copy) && e.KeyStates.HasFlag(DragDropKeyStates.ControlKey))
            || (!allowed.HasFlag(DragDropEffects.Move) && e.KeyStates.HasFlag(DragDropKeyStates.ShiftKey))
            || (!allowed.HasFlag(DragDropEffects.Link) && e.KeyStates.HasFlag(DragDropKeyStates.AltKey)))
        {
            e.Effects = DragDropEffects.None;
        }

        CopyPaste.DropEffect =
        CopyPaste.CurrentDropEffect = e.Effects;

        if (FileActions.IsAppDrive)
        {
            // Incoming APK / APKBKP install: always the default package large icon, not the
            // Windows shell APK glyph or a selected package's parsed launcher icon.
            if (!CopyPaste.IsSelf && FileHelper.AllFilesAreApks(CopyPaste.DragFiles))
            {
                CopyPaste.DragBitmap = DefaultAndroidPackageIcon.Bitmap;
            }
            else
            {
                // Outgoing package drag: CurrentFiles are APK paths whose DragImage is the shell placeholder.
                var packageIcon = ActiveSelectedItems.OfType<Package>().Select(p => p.Icon).FirstOrDefault(i => i is not null)
                    ?? Data.SelectedPackages.Select(p => p.Icon).FirstOrDefault(i => i is not null);
                if (packageIcon is not null)
                    CopyPaste.DragBitmap = packageIcon;
            }
        }
        else if (CopyPaste.CurrentFiles.Any())
        {
            CopyPaste.DragBitmap = CopyPaste.CurrentFiles.First().DragImage;
        }

        e.Handled = true;
    }

    private void ExplorerGrid_ContextMenuClosing(object sender, ContextMenuEventArgs e)
    {
        ViewModel.IsMenuOpen = false;
    }

    private void ExplorerGrid_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        // ContentGrid has no headers, so the "clicked the header row" check only applies to the
        // classic (Details) grid; Content view is otherwise treated like Icon view here.
        if (!ViewModel.IsIconView && !ViewModel.IsContentView)
        {
            var point = Mouse.GetPosition(ExplorerGrid);
            if (point.Y < ColumnHeaderHeight || CopyPaste.WasDragging)
            {
                ViewModel.IsMenuOpen = false;
                e.Handled = true;
                ClearWasDraggingAfterContext();
                return;
            }
        }
        else if (CopyPaste.WasDragging)
        {
            ViewModel.IsMenuOpen = false;
            e.Handled = true;
            ClearWasDraggingAfterContext();
            return;
        }

        ViewModel.IsMenuOpen = true;
        FileActionLogic.UpdateFileActions();
        ExplorerContextMenu.UpdateSeparators();

        if (e.Source is FrameworkElement target)
            target.ContextMenu = CreateRowContextMenu();
    }

    private void ClearWasDraggingAfterContext()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (CopyPaste.DragStatus is not CopyPasteService.DragState.Active)
                CopyPaste.WasDragging = false;
        }, DispatcherPriority.Input);
    }

    private AdbContextMenu CreateRowContextMenu() => new()
    {
        Style = TryFindResource("ExplorerContextMenuStyle") as Style,
    };

    private void ExplorerGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        if (RowHeight is null && ExplorerGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow row)
            RowHeight = row.ActualHeight;

        // Left-button only - see the matching comment in DataGridCell_PreviewMouseDown: resetting
        // this for a right-click would clobber the flag ExplorerGrid_ContextMenuOpening relies on
        // to suppress the context menu right after a drag was cancelled via right-click.
        if (e.ChangedButton is MouseButton.Left)
            CopyPaste.WasDragging = false;

        var mouseDownSource = e.OriginalSource as DependencyObject;
        var mouseDownRow = HitTestHelper.FindAncestor<DataGridRow>(mouseDownSource);
        TrackExplorerMouseDown(e, mouseDownSource, mouseDownRow is not null && mouseDownRow.IsSelected);

        if (HitTestHelper.IsInScrollBar(e.OriginalSource as DependencyObject))
        {
            MouseDownPoint = NullPoint;
            return;
        }

        var gridPoint = e.GetPosition(ExplorerGrid);

        int selectionIndex = ExplorerGrid.SelectedIndex;

        var actualRowWidth = ExplorerGrid.Columns
            .Where(col => col.Visibility == Visibility.Visible)
            .Sum(item => item.ActualWidth);

        var source = e.OriginalSource as DependencyObject;
        var onHeader = HitTestHelper.FindAncestor<DataGridColumnHeader>(source) is not null;
        var onRow = mouseDownRow is not null;
        var rightOfRows = gridPoint.X > actualRowWidth || gridPoint.X > DataGridContentWidth;

        if (!onHeader && (!onRow || rightOfRows))
        {
            if (ExplorerGrid.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if ((e.ChangedButton is MouseButton.Right || !SuppressExplorerUnselect)
                && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                ClearDataItemSelectionFlags();
                ExplorerGrid.UnselectAll();
                ExplorerGrid.SelectedIndex =
                selectionIndex = -1;
            }
        }

        ViewModel.CurrentSelectedIndex = selectionIndex;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = selectionIndex;
        }
    }

    /// <summary>
    /// Content view's analog of <see cref="ExplorerGrid_MouseDown"/>. Its single template column
    /// fills the full row width, so there is no "right of the columns" empty space to special-case
    /// like the classic grid's <c>rightOfRows</c> check — a click is either on a row or on empty
    /// space below the last row.
    /// </summary>
    private void ContentGrid_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        if (RowHeight is null && ContentGrid.ItemContainerGenerator.ContainerFromIndex(0) is DataGridRow row)
            RowHeight = row.ActualHeight;

        // Left-button only - see the matching comment in DataGridCell_PreviewMouseDown.
        if (e.ChangedButton is MouseButton.Left)
            CopyPaste.WasDragging = false;

        var mouseDownSource = e.OriginalSource as DependencyObject;
        var mouseDownRow = HitTestHelper.FindAncestor<DataGridRow>(mouseDownSource);
        TrackExplorerMouseDown(e, mouseDownSource, mouseDownRow is not null && mouseDownRow.IsSelected);

        if (HitTestHelper.IsInScrollBar(e.OriginalSource as DependencyObject))
        {
            MouseDownPoint = NullPoint;
            return;
        }

        int selectionIndex = ContentGrid.SelectedIndex;

        var source = e.OriginalSource as DependencyObject;
        var onRow = mouseDownRow is not null;

        if (!onRow)
        {
            if (ContentGrid.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if ((e.ChangedButton is MouseButton.Right || !SuppressExplorerUnselect)
                && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                ClearDataItemSelectionFlags();
                ContentGrid.UnselectAll();
                ContentGrid.SelectedIndex =
                selectionIndex = -1;
            }
        }

        ViewModel.CurrentSelectedIndex = selectionIndex;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = selectionIndex;
        }
    }

    private void ExplorerGrid_MouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var grid = ActiveDataGrid;
        var point = e.GetPosition(SelectionRect);
        bool withinEditingCell = false;
        DataGridCell cell = grid.SelectedCells.Count > 0
                            ? CellConverter.GetDataGridCell(grid.SelectedCells[Math.Min(1, grid.SelectedCells.Count - 1)])
                            : null;

        if (IsInEditMode && cell is not null)
        {
            withinEditingCell = VisualTreeHelper.GetDescendantBounds(cell).Contains(e.GetPosition(cell));
        }

        var abortDrag = e.LeftButton == MouseButtonState.Released
            || !RuntimeSettings.IsExplorerLoaded
            || MouseDownPoint == NullPoint
            || withinEditingCell
            || SuppressExplorerMarquee
            || (!SelectionRect.IsActive && HitTestHelper.IsInScrollBar(e.OriginalSource as DependencyObject));

        TryBeginExplorerDragOrMarquee(point, abortDrag, ActiveScrollViewer, cell);
    }

    private void InitiateDrag(DependencyObject dragSource)
    {
        IEnumerable<FileClass> selectedItems;
        VirtualFileDataObject? vfdo;
        if (FileActions.IsAppDrive)
        {
            vfdo = VirtualFileDataObject.PrepareTransfer(ActiveSelectedItems.Cast<Package>());
            selectedItems = VirtualFileDataObject.SelfFiles!;
        }
        else
        {
            selectedItems = ActiveSelectedItems.Cast<FileClass>();
            // Archive extract is copy-only (no cut / symlink from inside an archive).
            var effects = FileActions.IsArchive
                ? DragDropEffects.Copy
                : DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

            vfdo = VirtualFileDataObject.PrepareTransfer(selectedItems, effects);
            if (FileActions.IsArchive && vfdo is not null)
                vfdo.PreferredDropEffect = DragDropEffects.Copy;
        }

        if (vfdo is null)
            return;

        CopyPaste.DragStatus = CopyPasteService.DragState.Active;
        CopyPaste.WasDragging = true;
        CopyPaste.UpdateSelfVFDO(true);

        if (FileActions.IsAppDrive)
        {
            var package = ActiveSelectedItems.OfType<Package>().FirstOrDefault();
            // Prefer the parsed launcher icon already shown in the tile (not APK shell / placeholder).
            CopyPaste.DragBitmap = package?.Icon
                ?? VirtualFileDataObject.SelfFiles?.FirstOrDefault()?.ApkIcon
                ?? package?.IconViewModel.LargeIcon;
        }
        else
        {
            CopyPaste.DragBitmap = selectedItems.First().DragImage;
        }

        DragAutoScroll.Register(ActiveScrollViewer);
        DragAutoScroll.Begin();
        try
        {
            vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.DragDrop, dragSource, vfdo.PreferredDropEffect ?? DragDropEffects.Copy);
        }
        finally
        {
            DragAutoScroll.End();
            // Escape (and other OLE cancels) leave the button down; drop the original
            // mouse-down so MouseMove cannot start a rubber-band from that point.
            MouseDownPoint = NullPoint;
            SelectionRect.Collapse();
        }
    }

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isSyncingSelection)
            return;

        CommitRenameIfDeselected();

        if (ActiveSelectedItems.Count > 0 && !RuntimeSettings.IsExplorerLoaded)
        {
            ActiveUnselectAll();
            return;
        }

        if (!ViewModel.SelectionInProgress)
        {
            if (ActiveSelectedItems.Count == 1)
            {
                ViewModel.CurrentSelectedIndex = ActiveView.SelectedIndex;
                if (ViewModel.FirstSelectedIndex < 0
                    || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
                {
                    ViewModel.FirstSelectedIndex = ActiveView.SelectedIndex;
                }
            }
            else if (ActiveSelectedItems.Count > 1 && e.AddedItems.Count == 1)
            {
                ViewModel.CurrentSelectedIndex = ActiveView.Items.IndexOf(e.AddedItems[0]);
            }
        }

        SyncSelectionToOtherView(sender, e);

        bool isOngoingMultiSelection = SelectionRect.IsActive
                || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);

        if (!isOngoingMultiSelection && ActiveSelectedItems.Count <= 1)
        {
            SelectionTimer.Stop();
            ApplySelectionEffects();
        }
        else if (!SelectionTimer.IsEnabled)
        {
            SelectionTimer.Start();
        }
    }

    /// <summary>
    /// Returns the SelectedItems collection of a list/grid view (<see cref="IconView"/>,
    /// <see cref="ExplorerGrid"/>, or <see cref="ContentGrid"/>), or <see langword="null"/> for
    /// anything else.
    /// </summary>
    private static System.Collections.IList? SelectedItemsOf(object view) => view switch
    {
        AdbListView listView => listView.SelectedItems,
        DataGrid grid => grid.SelectedItems,
        _ => null,
    };

    /// <summary>
    /// Keeps selection consistent across all three overlapping list views (<see cref="IconView"/>,
    /// <see cref="ExplorerGrid"/>, <see cref="ContentGrid"/>) — only one is visible at a time, but
    /// the other two are kept in sync so switching the active view (or view mode) does not lose or
    /// stale the selection. Also fixes <c>IsSelected</c> on the underlying data items (packages or
    /// files) for virtualized containers that had no container when <c>UnselectAll()</c> was
    /// called, and were therefore skipped by the TwoWay binding propagation.
    /// </summary>
    private void SyncSelectionToOtherView(object sender, SelectionChangedEventArgs e)
    {
        if (SelectedItemsOf(sender) is not { } sourceItemsList)
            return;

        var sourceItems = sourceItemsList.Cast<object>().ToList();
        var sourceSet = new HashSet<object>(sourceItems);

        if (FileActions.IsAppDrive)
        {
            // Fix IsSelected on Package items whose containers were recycled by virtualization
            // so UnselectAll() could not propagate through the TwoWay binding.
            var packages = Data.Packages ?? ActiveView.Items.OfType<Package>();
            foreach (var pkg in packages)
            {
                var shouldSelect = sourceSet.Contains(pkg);
                if (pkg.IsSelected != shouldSelect)
                    pkg.IsSelected = shouldSelect;
            }
        }
        else
        {
            // Fix IsSelected on underlying data items for virtualized containers
            // that had no container when UnselectAll() was called, and were therefore
            // skipped by the TwoWay binding propagation.
            var files = DirList?.FileList ?? ExplorerGrid.Items.OfType<FilePath>();
            foreach (var item in files)
            {
                var shouldSelect = sourceSet.Contains(item);
                if (item.IsSelected != shouldSelect)
                    item.IsSelected = shouldSelect;
            }
        }

        // Keep the other (hidden) view(s) in sync when switching between details / icon / content view.
        _isSyncingSelection = true;
        try
        {
            Selector[] allViews = [IconView, ExplorerGrid, ContentGrid];
            foreach (var view in allViews)
            {
                if (ReferenceEquals(view, sender) || SelectedItemsOf(view) is not { } targetItems)
                    continue;

                var toRemove = targetItems.Cast<object>()
                    .Where(item => !sourceSet.Contains(item))
                    .ToList();
                foreach (var item in toRemove)
                    targetItems.Remove(item);

                foreach (var item in sourceItems)
                {
                    if (!targetItems.Contains(item))
                        targetItems.Add(item);
                }
            }
        }
        finally
        {
            _isSyncingSelection = false;
        }
    }

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

        var currentDirection = sortedColumn == ViewModel.SortedColumn ? ViewModel.SortDirection : null;
        var direction = ListHelper.Invert(currentDirection);
        ViewModel.SetSort(sortedColumn, direction);

        e.Column.SortDirection = direction;
        e.Handled = true;
    }

    /// <summary>
    /// Walks up from <paramref name="source"/> checking for <paramref name="element"/> among its
    /// visual/logical ancestors - used to tell a click on the active rename TextBox itself apart
    /// from a click elsewhere in the same (currently-renaming) row.
    /// </summary>
    private static bool IsWithinElement(DependencyObject? source, DependencyObject element)
    {
        for (var dep = source; dep is not null; dep = HitTestHelper.GetVisualOrLogicalParent(dep))
        {
            if (ReferenceEquals(dep, element))
                return true;
        }

        return false;
    }

    private void BeginRename(TextBox textBox) => _renameTextBox = textBox;

    private void ClearRename() => _renameTextBox = null;

    private void CommitRenameIfDeselected()
    {
        if (_renameTextBox?.DataContext is not FileClass file)
        {
            ClearRename();
            return;
        }

        var vm = ViewModel.IsIconView ? (FileViewModelBase)file.IconViewModel : file.FolderViewModel;
        if (!vm.IsInEditMode)
        {
            ClearRename();
            return;
        }

        if (ActiveSelectedItems.Count == 1 && ReferenceEquals(ActiveSelectedItems[0], file))
            return;

        FileViewModelBase.RenameCommit(_renameTextBox, ViewModel.IsIconView ? ExitIconEditMode : ExitFolderEditMode);
    }

    private void ExitFolderEditMode(FileClass file)
    {
        file.FolderViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void ExitIconEditMode(FileClass file)
    {
        file.IconViewModel.IsInEditMode = false;
        FileActions.IsExplorerEditing = false;
        ClearRename();
    }

    private void NameColumnEdit_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        FileViewModelBase.RenameKeyDown(textBox, e.Key, ExitFolderEditMode);
        if (e.Key is Key.Escape or Key.F2 or Key.Enter)
            e.Handled = true;
    }

    private void NameColumnEdit_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is not true)
            return;

        var textBox = sender as TextBox;

        if (textBox.DataContext is FileClass file)
        {
            FileViewModelBase.PrepareRenameTextBox(textBox);
            BeginRename(textBox);
            Owner.ShowRenameTooltip(textBox, file.FolderViewModel);
        }
    }

    private void IconView_RenameStarted(object? sender, TextBox textBox)
    {
        BeginRename(textBox);
        if (textBox.DataContext is FileClass file)
            Owner.ShowRenameTooltip(textBox, file.IconViewModel, centerHorizontally: true);
    }

    private void NameColumnEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (textBox.DataContext is FileClass file && !file.FolderViewModel.IsInEditMode)
            return;

        FileViewModelBase.RenameCommit(textBox, ExitFolderEditMode);
    }

    private void NameColumnEdit_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        FileViewModelBase.RenameTextChanged(textBox);
    }

    private void SelectionRect_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SelectionRect.IsActive || SelectionRect.SelectionOccurred)
            e.Handled = true;

        SelectionRect.Collapse();

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = ViewModel.NextSelectedIndex;
        }
    }

    private void SelectionRect_MouseMove(object sender, MouseEventArgs e)
    {
        if (ViewModel.IsIconView)
            IconView_MouseMove(sender, e);
        else
            ExplorerGrid_MouseMove(sender, e);
    }

    internal void EndExplorerMouseGesture()
    {
        if (SelectionRect.IsActive)
            SelectionRect.Collapse();

        MouseDownPoint = NullPoint;
        CopyPaste.WasDragging = false;
        Owner.SuppressSelectionAfterMenu = false;
    }

    private void IconView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        // Left-button only - see the matching comment in DataGridCell_PreviewMouseDown.
        if (e.ChangedButton is MouseButton.Left)
            CopyPaste.WasDragging = false;

        // Walk up from the original source to determine if the click is on an item or empty space
        var source = e.OriginalSource as DependencyObject;
        var hitItem = source is not null
            ? ItemsControl.ContainerFromElement(IconView, source) as ListViewItem
            : null;

        TrackExplorerMouseDown(e, source, hitItem is not null && hitItem.IsSelected);

        int selectionIndex = IconView.SelectedIndex;

        if (hitItem is not null)
        {
            if (Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                // Exclusive select on left/right click of an unselected item. ListView UnselectAll
                // cannot clear IsSelected on recycled (off-screen) containers via TwoWay binding,
                // which otherwise leaves a second item selected after a single click.
                if (!hitItem.IsSelected)
                {
                    SelectOnlyItem(hitItem.DataContext);
                    if (e.ChangedButton is MouseButton.Right)
                        e.Handled = true;
                    selectionIndex = IconView.SelectedIndex;
                }
            }
        }
        else
        {
            // Ignore clicks on scrollbars — do not keep MouseDownPoint or marquee starts
            // when the captured thumb's MouseMove bubbles over the viewport.
            if (HitTestHelper.IsInScrollBar(source))
            {
                MouseDownPoint = NullPoint;
                return;
            }

            if (IconView.SelectedItems.Count > 0 && IsInEditMode)
                IsInEditMode = false;

            if ((e.ChangedButton is MouseButton.Right || !SuppressExplorerUnselect)
                && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
            {
                ClearDataItemSelectionFlags();
                IconView.UnselectAll();
                selectionIndex = -1;
            }
        }

        ViewModel.CurrentSelectedIndex = selectionIndex;

        if (ViewModel.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            ViewModel.FirstSelectedIndex = selectionIndex;
        }
    }

    private void IconView_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        if (!SelectionRect.IsActive && !SelectionRect.SelectionOccurred)
            return;

        SelectionRect.Collapse();
        e.Handled = true;
    }

    private void IconView_MouseMove(object sender, MouseEventArgs e)
    {
        if (Mouse.LeftButton is MouseButtonState.Released)
            CopyPaste.ClearDrag();

        var point = e.GetPosition(SelectionRect);

        var abortDrag = e.LeftButton == MouseButtonState.Released
            || !RuntimeSettings.IsExplorerLoaded
            || MouseDownPoint == NullPoint
            || SuppressExplorerMarquee
            || (!SelectionRect.IsActive && HitTestHelper.IsInScrollBar(e.OriginalSource as DependencyObject));

        DependencyObject dragSource = IconView;
        if (IconView.SelectedItems.Count > 0)
            dragSource = IconView.ItemContainerGenerator.ContainerFromItem(IconView.SelectedItems[0]) as DependencyObject ?? IconView;

        TryBeginExplorerDragOrMarquee(point, abortDrag, IconView.ScrollViewer, dragSource);
    }

    internal void OnThumbsSizeChanged()
    {
        var size = RuntimeSettings.ThumbsSize;
        if (size != ThumbnailService.ThumbnailSize.Disabled)
            ExplorerPageHeader.InvalidateFileIcons();

        if (ActiveSelectedItems.Count > 0)
            ScheduleKeepFirstSelectedInView();
        else
        {
            App.SafeBeginInvoke(() => ActiveScrollViewer?.ScrollToTop(), DispatcherPriority.Loaded);
        }
    }

    private void IconView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged || !ViewModel.IsIconView || ActiveSelectedItems.Count == 0)
            return;

        ScheduleKeepFirstSelectedInView();
    }

    private void DriveList_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged
            || !FileActions.IsDriveViewVisible
            || DriveList.SelectedItem is null)
            return;

        var generation = ++_keepSelectionInViewGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _keepSelectionInViewGeneration)
                return;
            if (DriveList.SelectedItem is not null)
                DriveList.ScrollIntoView(DriveList.SelectedItem);
        }, DispatcherPriority.Loaded);
    }

    private const double ContentNameComfortableWidth = 350;
    private const double ContentTypeEstimatedWidth = 160;
    private const double ContentDateSizeFullWidth = 250;
    private const double ContentDateSizeCollapsedWidth = 130;
    private const double ContentTypeColumnHideWidth =
        ContentNameComfortableWidth + ContentDateSizeFullWidth + ContentTypeEstimatedWidth;
    private const double ContentDateLineHideWidth =
        ContentNameComfortableWidth + ContentDateSizeFullWidth;
    private const double ContentDateSizeColumnHideWidth = 400;

    public static readonly DependencyProperty ContentDateLineVisibilityProperty =
        DependencyProperty.Register(
            nameof(ContentDateLineVisibility),
            typeof(Visibility),
            typeof(ExplorerListHost),
            new PropertyMetadata(Visibility.Visible));

    public Visibility ContentDateLineVisibility
    {
        get => (Visibility)GetValue(ContentDateLineVisibilityProperty);
        set => SetValue(ContentDateLineVisibilityProperty, value);
    }

    private void ContentGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!e.WidthChanged)
            return;

        var generation = ++_contentColumnsGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _contentColumnsGeneration)
                return;

            ApplyContentColumnLayout(ContentGrid.ActualWidth);
        }, DispatcherPriority.Loaded);
    }

    private void ApplyContentColumnLayout(double width)
    {
        ContentTypeColumn.Visibility = width < ContentTypeColumnHideWidth
            ? Visibility.Collapsed
            : Visibility.Visible;

        ContentDateSizeColumn.Visibility = width < ContentDateSizeColumnHideWidth
            ? Visibility.Collapsed
            : Visibility.Visible;

        var showDateLine = width >= ContentDateLineHideWidth;
        ContentDateSizeColumn.Width = new(showDateLine ? ContentDateSizeFullWidth : ContentDateSizeCollapsedWidth);
        ContentDateLineVisibility = showDateLine ? Visibility.Visible : Visibility.Hidden;
    }

    /// <summary>
    /// After a wrap-panel width change, ensure the first selected explorer item is still visible.
    /// </summary>
    private void ScheduleKeepFirstSelectedInView()
    {
        var generation = ++_keepSelectionInViewGeneration;
        App.SafeBeginInvoke(() =>
        {
            if (generation != _keepSelectionInViewGeneration)
                return;
            if (ActiveSelectedItems.Count > 0)
                ActiveScrollIntoView(ActiveSelectedItems[0]);
        }, DispatcherPriority.Loaded);
    }

    private void EmptyNonRootTextBlock_Loaded(object sender, RoutedEventArgs e) => TextHelper.BuildLocalizedInlines(sender, e);

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

    // Wired at each source (list + item containers), not just this ScrollViewer - see EventSetter usages.
    private void DriveScrollViewer_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
        => e.Handled = true;
}
