using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

/// <summary>
/// Drive cards act like their navigation tree drive nodes: the same context menu, drop rules and targets.
/// </summary>
public partial class ExplorerListHost
{
    private IDisposable? _driveMenuScope;

    private void DriveItem_ContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DriveViewModel drive, ContextMenu: AdbContextMenu menu }
            || FileActionLogic.DriveTreeNode(drive) is not { } node
            || FileList.FromTreeNode(node) is not { } list)
        {
            e.Handled = true;
            return;
        }

        // New folder and saved-location changes stay tree-only; the drive view has no place to show them.
        list.ForbidNew = true;
        list.Actions.AddSavedLocationEnabled = false;
        list.Actions.RemoveSavedLocationEnabled = false;

        // Like a file, a right-clicked drive becomes the selection, which key gestures act on.
        drive.IsSelected = true;

        EndDriveMenuScope();
        _driveMenuScope = Data.Use(list);
        FileActionLogic.SetTreeContextTarget(node);
        FileActionLogic.UpdateFileActions(list);
        ExplorerContextMenu.UpdateSeparators(showEmptyPlaceholder: false);

        if (ExplorerContextMenu.VisibleList.Count == 0)
        {
            EndDriveMenuScope();
            e.Handled = true;
            return;
        }

        menu.Closed -= DriveContextMenu_Closed;
        menu.Closed += DriveContextMenu_Closed;
    }

    private void DriveContextMenu_Closed(object sender, RoutedEventArgs e)
    {
        if (sender is ContextMenu menu)
            menu.Closed -= DriveContextMenu_Closed;

        // Deferred like the tree's menu, so the clicked item's command still runs on the drive.
        Dispatcher.BeginInvoke(() =>
        {
            EndDriveMenuScope();
            FileActionLogic.UpdateFileActions();
        }, DispatcherPriority.Loaded);
    }

    private void EndDriveMenuScope()
    {
        if (_driveMenuScope is null)
            return;

        _driveMenuScope.Dispose();
        _driveMenuScope = null;
        FileActionLogic.SetTreeContextTarget(null);
    }

    /// <summary>The drive pressed on, which a move far enough drags; null when no drag can start.</summary>
    private DriveViewModel? _pressedDrive;

    private Point _drivePressPoint;

    private void DriveItem_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _pressedDrive = null;

        // A double click opens the drive instead
        if (e.ClickCount > 1 || sender is not FrameworkElement { DataContext: DriveViewModel drive })
            return;

        _pressedDrive = drive;
        _drivePressPoint = e.GetPosition(this);
    }

    private void DriveItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => _pressedDrive = null;

    /// <summary>A drive card drags as its tree node does: as its root folder, shown by the drive's icon and name.</summary>
    private void DriveItem_PreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_pressedDrive is not { } drive || sender is not UIElement item)
            return;

        if (e.LeftButton is not MouseButtonState.Pressed)
        {
            _pressedDrive = null;
            return;
        }

        if ((e.GetPosition(this) - _drivePressPoint).LengthSquared < AdbExplorerConst.DRAG_START_DISTANCE_SQUARED)
            return;

        _pressedDrive = null;

        if (FileActionLogic.DriveTreeNode(drive) is not { } node || !NodeDrag.CanDrag(node))
            return;

        // The card holds the mouse while pressed, which the drag takes over
        Mouse.Capture(null);
        NodeDrag.Run(item, node);
        e.Handled = true;
    }

    private void DriveItem_DragOver(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { DataContext: DriveViewModel drive })
            return;

        if (FileActionLogic.DriveTreeNode(drive) is not { } node)
        {
            e.Effects = DragDropEffects.None;
            return;
        }

        drive.IsDragOver = CopyPaste.ApplyTreeDropEffects(e, node) is not DragDropEffects.None;
    }

    private void DriveItem_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: DriveViewModel drive })
            drive.IsDragOver = false;

        e.Handled = true;
    }

    private void DriveItem_Drop(object sender, DragEventArgs e)
    {
        e.Handled = true;

        if (sender is not FrameworkElement { DataContext: DriveViewModel drive })
            return;

        drive.IsDragOver = false;

        if (FileActionLogic.DriveTreeNode(drive) is not { } node)
            return;

        CopyPaste.ApplyTreeDropEffects(e, node);
        if (e.Effects is not DragDropEffects.None)
            CopyPaste.AcceptTreeDrop(e, node);
    }
}
