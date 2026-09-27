using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

public partial class ExplorerListHost
{
    /// <summary>
    /// Links this control back to its owning <see cref="ExplorerPageContent"/>. Must be called
    /// once, immediately after construction, before any user interaction can reach this control.
    /// </summary>
    /// <summary>Middle-click on a folder, archive or drive opens it in a new tab - and in search mode, any item's folder.</summary>
    private void ExplorerListHost_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Middle || e.OriginalSource is not DependencyObject source)
            return;

        if (Owner.Instance.EffectiveDevice is not { } device || FileActions.ListingInProgress)
            return;

        var item = FindItemUnder(source);
        var path = FileActionLogic.GetNewTabPath(item, device, FileActions.IsAppDrive, FileActions.IsArchive, FileActions.IsSearchMode);

        if (string.IsNullOrEmpty(path))
            return;

        e.Handled = true;
        DeviceHelper.OpenDeviceInNewTab(device, new AdbLocation(path));
    }

    private object? FindItemUnder(DependencyObject source)
    {
        for (var current = source; current is not null && !ReferenceEquals(current, this); current = current is Visual ? VisualTreeHelper.GetParent(current) : LogicalTreeHelper.GetParent(current))
        {
            if (current is DataGridRow row)
                return row.Item;

            if (current is ListBoxItem listItem)
                return listItem.DataContext;
        }

        return null;
    }

    /// <summary>
    /// Clears the tracked mouse-down point unless a marquee/drag is in progress. Bridges
    /// <see cref="ExplorerPageContent"/>'s window-level <c>Grid_MouseEnter</c> handler to this
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
        if (Owner.Instance.IsContentView)
        {
            iconColumn = ContentNameColumn;
            nameColumn = ContentNameColumn;
            packageColumn = null;
        }

        return HitTestHelper.IsExplorerNameOrIconHit(
            originalSource,
            positionInSelectionRect,
            SelectionRect,
            Owner.Instance.IsIconView,
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
            SelectionRect.Update(point, MouseDownPoint, scroller, ActiveView, ActiveSelectedItems, Owner.Instance);
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
        SelectionRect.Update(point, MouseDownPoint, scroller, ActiveView, ActiveSelectedItems, Owner.Instance);
    }

    private void DataGridCell_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left and not MouseButton.Right)
            return;

        SelectionRect.ResetGesture();

        Owner.PathBoxFocus(false);
        RaiseUnfocusSearchBox();

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        // A click on an embedded interactive control (e.g. the "View size" button) must reach it
        // untouched, rather than being consumed here as a row/selection gesture.
        if (HitTestHelper.FindAncestor<ButtonBase>(e.OriginalSource as DependencyObject) is not null)
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
                RenameBox.FileCommit(_renameTextBox, ExitFolderEditMode);

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

        if (!row.IsSelected
            && Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            SelectOnlyItem(row.Item);
        }

        Owner.Instance.NextSelectedIndex = current;
        Owner.Instance.CurrentSelectedIndex = current;
        if (ActiveSelectedItems.Count < 1)
            Owner.Instance.FirstSelectedIndex = current;
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
            && ActiveDevice is { } device
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
            ClearWasDraggingIfIdle();
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
        Owner.Instance.CurrentSelectedIndex = current;

        if (MultiRowSelect(row))
            return true;

        if (Owner.Instance.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            Owner.Instance.FirstSelectedIndex = current;
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

        if (!ActiveDevice.HasRootShell
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

    private void DataGridRow_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton is not MouseButton.Left)
            return;

        SelectionRect.ResetGesture();

        Owner.PathBoxFocus(false);
        RaiseUnfocusSearchBox();

        if (e.OriginalSource is Border)
        {
            ClickCount = -1;
            return;
        }

        CopyPaste.WasDragging = false;
        var row = sender as DataGridRow;

        TrackExplorerMouseDown(e, e.OriginalSource as DependencyObject, row is not null && row.IsSelected);

        Owner.Instance.SetIndexSingle(row.GetIndex());
    }

    private void ItemContainer_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton != MouseButton.Left || IsInEditMode || ActiveSelectedItems.Count != 1)
            return;

        ClickCount = -1;
        DoubleClick(ActiveView.SelectedItem);
    }

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

        Owner.Instance.CurrentSelectedIndex = selectionIndex;

        if (Owner.Instance.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            Owner.Instance.FirstSelectedIndex = selectionIndex;
        }
    }

    /// <summary>
    /// Content view's analog of <see cref="ExplorerGrid_MouseDown"/>. Its single template column
    /// fills the full row width, so there is no "right of the columns" empty space to special-case
    /// like the classic grid's <c>rightOfRows</c> check - a click is either on a row or on empty
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

        Owner.Instance.CurrentSelectedIndex = selectionIndex;

        if (Owner.Instance.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            Owner.Instance.FirstSelectedIndex = selectionIndex;
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
                            ? DataGridHelper.GetDataGridCell(grid.SelectedCells[Math.Min(1, grid.SelectedCells.Count - 1)])
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

    internal void EndExplorerMouseGesture()
    {
        if (SelectionRect.IsActive)
            SelectionRect.Collapse();

        MouseDownPoint = NullPoint;
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

        WasEditing = hitItem?.DataContext is FileClass clickedFile && clickedFile.IconViewModel.IsInEditMode;
        if (WasEditing)
        {
            // A click anywhere else on the tile currently being renamed - not on the rename TextBox
            // itself - should commit the rename, matching DataGridCell_PreviewMouseDown.
            if (_renameTextBox is not null && !IsWithinElement(source, _renameTextBox))
                RenameBox.FileCommit(_renameTextBox, ExitIconEditMode);

            return;
        }

        var itemAlreadySelected = hitItem is not null && hitItem.IsSelected;
        FileIconView.ItemWasSelectedBeforeClick = itemAlreadySelected;

        TrackExplorerMouseDown(e, source, itemAlreadySelected);

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
            // Ignore clicks on scrollbars - do not keep MouseDownPoint or marquee starts
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

        Owner.Instance.CurrentSelectedIndex = selectionIndex;

        if (Owner.Instance.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            Owner.Instance.FirstSelectedIndex = selectionIndex;
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
}
