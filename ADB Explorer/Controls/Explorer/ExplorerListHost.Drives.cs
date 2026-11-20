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
