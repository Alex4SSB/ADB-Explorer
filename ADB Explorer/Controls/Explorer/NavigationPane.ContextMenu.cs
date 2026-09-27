namespace ADB_Explorer.Controls;

public partial class NavigationPane
{
    private NavigationTreeNode? _contextTarget;

    private IDisposable? _treeMenuScope;

    private bool _holdSelectSuppressForMenu;

    private void TreeViewItem_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not TreeViewItem || e.OriginalSource is not DependencyObject source)
            return;

        if (!ReferenceEquals(FindOwningTreeViewItem(source), sender))
            return;

        if (!_holdSelectSuppressForMenu)
        {
            NavigationTreeNode.SuppressUserSelectFromExpander++;
            _holdSelectSuppressForMenu = true;
        }

        _selectionBeforeExpander = FindSelectedNode(TreeItems);
        Dispatcher.BeginInvoke(RestoreSelectionKeepSuppress, DispatcherPriority.Input);
        Dispatcher.BeginInvoke(ReleaseRightClickSuppressIfNoMenu, DispatcherPriority.ApplicationIdle);
    }

    private void TreeViewItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (Data.CopyPaste.WasDragging)
        {
            e.Handled = true;

            // Swallow this opening (cancel-drag click, including nested TreeViewItems), then allow the next one.
            Dispatcher.BeginInvoke(() =>
            {
                // A button may still be held, its release yet to come.
                if (Data.CopyPaste.DragStatus is not CopyPasteService.DragState.Active
                    && !MouseState.IsAnyButtonDown)
                {
                    Data.CopyPaste.WasDragging = false;
                }
            }, DispatcherPriority.Input);
            return;
        }

        if (sender is not TreeViewItem item || e.OriginalSource is not DependencyObject source)
        {
            e.Handled = true;
            return;
        }

        if (!ReferenceEquals(FindOwningTreeViewItem(source), item))
            return;

        if (item.DataContext is not NavigationTreeNode node)
        {
            e.Handled = true;
            return;
        }

        EndTreeMenuScope();
        SetContextTarget(node);

        if (item.ContextMenu is not AdbContextMenu menu)
            return;

        menu.Closed -= TreeContextMenu_Closed;
        menu.Closed += TreeContextMenu_Closed;

        if (node.Device is { } device)
        {
            DeviceContextMenu.SetFor(device);
            if (DeviceContextMenu.VisibleList.Count == 0)
            {
                CancelTreeContextMenu(menu, e);
                return;
            }

            menu.Style = TryFindResource("DeviceContextMenuStyle") as Style;
            return;
        }

        var list = FileList.FromTreeNode(node);
        if (list is null)
        {
            CancelTreeContextMenu(menu, e);
            return;
        }

        _treeMenuScope = Data.Use(list);
        FileActionLogic.UpdateFileActions(list);
        ExplorerContextMenu.UpdateSeparators(showEmptyPlaceholder: false);
        if (ExplorerContextMenu.VisibleList.Count == 0)
        {
            CancelTreeContextMenu(menu, e);
            return;
        }

        menu.Style = TryFindResource("TreeContextMenuStyle") as Style;
    }

    private void CancelTreeContextMenu(AdbContextMenu menu, ContextMenuEventArgs e)
    {
        menu.Closed -= TreeContextMenu_Closed;
        RestoreSelectionKeepSuppress();
        ReleaseRightClickSuppress();
        _selectionBeforeExpander = null;
        SetContextTarget(null);
        EndTreeMenuScope();
        e.Handled = true;
    }

    private void TreeContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            menu.Closed -= TreeContextMenu_Closed;

        RestoreSelectionKeepSuppress();
        ReleaseRightClickSuppress();
        _selectionBeforeExpander = null;
        SetContextTarget(null);
        if (Mouse.LeftButton is MouseButtonState.Pressed)
            _suppressTreeDragAfterMenu = true;
        Dispatcher.BeginInvoke(() =>
        {
            EndTreeMenuScope();
            FileActionLogic.UpdateFileActions();
            if (TreeVm?.HasQueuedEdit == true)
                TreeVm.StartQueuedEdit();
        }, DispatcherPriority.Loaded);
    }

    private void EndTreeMenuScope()
    {
        _treeMenuScope?.Dispose();
        _treeMenuScope = null;
    }

    private void SetContextTarget(NavigationTreeNode? node)
    {
        if (_contextTarget is not null)
            _contextTarget.IsContextTarget = false;

        _contextTarget = node;
        TreeVm?.SetContextTarget(node);
        if (node is not null)
            node.IsContextTarget = true;
    }

    private void RestoreSelectionKeepSuppress()
    {
        _selectionBeforeExpander?.SetSelected(true);
    }

    private void ReleaseRightClickSuppressIfNoMenu()
    {
        if (_contextTarget is not null)
            return;

        ReleaseRightClickSuppress();
    }

    private void ReleaseRightClickSuppress()
    {
        if (!_holdSelectSuppressForMenu)
            return;

        _holdSelectSuppressForMenu = false;
        if (NavigationTreeNode.SuppressUserSelectFromExpander > 0)
            NavigationTreeNode.SuppressUserSelectFromExpander--;
    }
}
