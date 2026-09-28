using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

public partial class ExplorerListHost
{
    internal void ActiveUnselectAll()
    {
        try
        {
            if (Owner.Instance.IsIconView)
                IconView.UnselectAll();
            else if (Owner.Instance.IsContentView)
                ContentGrid.UnselectAll();
            else
                ExplorerGrid.UnselectAll();
        }
        catch
        { }
    }

    internal void ActiveSelectAll()
    {
        if (Owner.Instance.IsIconView)
            IconView.SelectAll();
        else if (Owner.Instance.IsContentView)
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

    internal void InvertSelection()
    {
        var selected = ActiveSelectedItems.Cast<object>().ToHashSet();
        var inverted = ActiveView.Items.Cast<object>().Where(item => !selected.Contains(item)).ToList();

        CopySelection(ActiveView, inverted);
    }

    /// <summary>
    /// Skip rubber-band / file drag from this click (open menu, or the click that just closed one).
    /// </summary>
    private bool SuppressExplorerMarquee =>
        Owner.Instance.IsMenuOpen || Owner.ToolbarSubmenuDepth > 0 || Owner.SuppressSelectionAfterMenu;

    /// <summary>
    /// Skip clearing selection only while the explorer context menu is open.
    /// Toolbar submenu dismiss is handled by <see cref="SuppressExplorerMarquee"/> so
    /// empty-space unselect still runs on that click.
    /// </summary>
    private bool SuppressExplorerUnselect => Owner.Instance.IsMenuOpen;

    internal void CancelExplorerMarquee()
    {
        MouseDownPoint = NullPoint;
        CopyPaste.DragStatus = CopyPasteService.DragState.None;
        SelectionRect.Collapse();
    }

    private readonly DispatcherTimer SelectionTimer = new() { Interval = SELECTION_CHANGED_DELAY };

    /// <summary>
    /// Coalesces wrap-panel reflows (side pane open/close/resize) so we scroll once after layout.
    /// </summary>
    private int _keepSelectionInViewGeneration;

    private bool _isSyncingSelection = false;

    /// <summary>The view whose selection is current - a hidden view catches up to it only when shown.</summary>
    private Selector? _selectionSource;

    private void SelectionTimer_Tick(object? sender, EventArgs e)
    {
        SelectionTimer.Stop();

        // A split view's other pane was left since this fired; its effects would clobber the focused one's.
        if (ReferenceEquals(Owner.Instance, Data.ActiveExplorerInstance))
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

        if (ActiveDevice is { SupportsLsV2: false })
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
            // Prefer the full package list - ActiveView.Items may omit filtered/system packages
            // while their IsSelected flags still linger from virtualization. Snapshot first -
            // setting IsSelected can trigger a live filter/sort that mutates this collection.
            var packages = (Data.Packages ?? ExplorerGrid.Items.OfType<Package>()).ToList();
            foreach (var pkg in packages)
            {
                if (pkg.IsSelected)
                    pkg.IsSelected = false;
            }
            return;
        }

        if (DirList?.FileList is null)
            return;

        // Snapshot before enumerating - clearing IsSelected below can trigger a live
        // filter/sort that mutates FileList mid-loop ("Collection was modified").
        foreach (var file in DirList.FileList.ToList())
        {
            if (file.IsSelected)
                file.IsSelected = false;
        }
    }

    private bool MultiRowSelect(DataGridRow row)
    {
        var grid = (DataGrid)ItemsControl.ItemsControlFromItemContainer(row);
        var current = row.GetIndex();

        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            grid.UnselectAll();

            var firstSelected = Owner.Instance.FirstSelectedIndex;
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

    private void ExplorerGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // A hidden view's realized rows follow the files' IsSelected, and it catches up in full once shown.
        if (_isSyncingSelection || !ReferenceEquals(sender, ActiveView))
            return;

        CommitRenameIfDeselected();

        if (ActiveSelectedItems.Count > 0 && !RuntimeSettings.IsExplorerLoaded)
        {
            ActiveUnselectAll();
            return;
        }

        if (!Owner.Instance.SelectionInProgress)
        {
            if (ActiveSelectedItems.Count == 1)
            {
                Owner.Instance.CurrentSelectedIndex = ActiveView.SelectedIndex;
                if (Owner.Instance.FirstSelectedIndex < 0
                    || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
                {
                    Owner.Instance.FirstSelectedIndex = ActiveView.SelectedIndex;
                }
            }
            else if (ActiveSelectedItems.Count > 1 && e.AddedItems.Count == 1)
            {
                Owner.Instance.CurrentSelectedIndex = ActiveView.Items.IndexOf(e.AddedItems[0]);
            }
        }

        SyncSelectionFrom((Selector)sender);

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
    /// Makes <paramref name="source"/>'s selection current, and fixes <c>IsSelected</c> on the underlying data items
    /// (packages or files), which the TwoWay binding misses for virtualized items that have no container.
    /// </summary>
    private void SyncSelectionFrom(Selector source)
    {
        if (SelectedItemsOf(source) is not { } sourceItemsList)
            return;

        _selectionSource = source;
        SyncItemsIsSelected(sourceItemsList.Cast<object>().ToHashSet());
    }

    /// <summary>Brings the view just switched to (details / icon / content) up to the one it replaces.</summary>
    private void CatchUpActiveViewSelection()
    {
        var target = ActiveView;
        if (_selectionSource is null
            || ReferenceEquals(target, _selectionSource)
            || SelectedItemsOf(_selectionSource) is not { } sourceItemsList)
            return;

        var sourceItems = sourceItemsList.Cast<object>().ToList();

        _isSyncingSelection = true;
        try
        {
            CopySelection(target, sourceItems);
        }
        finally
        {
            _isSyncingSelection = false;
        }

        _selectionSource = target;
    }

    private void SyncItemsIsSelected(HashSet<object> sourceSet)
    {
        if (FileActions.IsAppDrive)
        {
            // Fix IsSelected on Package items whose containers were recycled by virtualization
            // so UnselectAll() could not propagate through the TwoWay binding. Snapshot first -
            // same "Collection was modified" risk as ClearDataItemSelectionFlags.
            var packages = (Data.Packages ?? ActiveView.Items.OfType<Package>()).ToList();
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
            // skipped by the TwoWay binding propagation. Snapshot first - same reason as above.
            var files = (DirList?.FileList ?? ExplorerGrid.Items.OfType<FilePath>()).ToList();
            foreach (var item in files)
            {
                var shouldSelect = sourceSet.Contains(item);
                if (item.IsSelected != shouldSelect)
                    item.IsSelected = shouldSelect;
            }
        }
    }

    /// <summary>Makes <paramref name="items"/> <paramref name="view"/>'s selection, as a single selection change.</summary>
    private static void CopySelection(Selector view, List<object> items)
    {
        if (items.Count == 0)
            UnselectAllOf(view);
        else if (items.Count == view.Items.Count)
            SelectAllOf(view);
        else if (view is AdbListView listView)
            listView.SelectOnly(items);
        else if (view is AdbDataGrid grid)
            grid.SelectOnly(items);
    }

    private static void SelectAllOf(Selector view)
    {
        if (view is AdbListView listView)
            listView.SelectAll();
        else if (view is DataGrid grid)
            grid.SelectAll();
    }

    private static void UnselectAllOf(Selector view)
    {
        if (view is AdbListView listView)
            listView.UnselectAll();
        else if (view is DataGrid grid)
            grid.UnselectAll();
    }

    private void SelectionRect_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (SelectionRect.IsActive || SelectionRect.SelectionOccurred)
            e.Handled = true;

        SelectionRect.Collapse();

        if (Owner.Instance.FirstSelectedIndex < 0
            || Keyboard.Modifiers is not ModifierKeys.Control and not ModifierKeys.Shift)
        {
            Owner.Instance.FirstSelectedIndex = Owner.Instance.NextSelectedIndex;
        }
    }

    private void SelectionRect_MouseMove(object sender, MouseEventArgs e)
    {
        if (Owner.Instance.IsIconView)
            IconView_MouseMove(sender, e);
        else
            ExplorerGrid_MouseMove(sender, e);
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
}
