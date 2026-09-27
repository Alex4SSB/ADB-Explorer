using static ADB_Explorer.Models.AdbExplorerConst;

namespace ADB_Explorer.Controls;

public partial class NavigationPane
{
    private bool _suppressTreeDragAfterMenu;

    private Point _treeDragStart;

    private NavigationTreeNode? _treeDragNode;

    private TreeViewItem? _treeDragItem;

    private bool _treeDragPending;

    private bool _treeDidDrag;

    private NavigationTreeNode? _treeDropHighlight;

    private bool CanStartTreeDrag(NavigationTreeNode node)
    {
        if (TreeVm?.IsTreeDragBlocked != false)
            return false;

        if (node.IsTemp || node.IsInEditMode)
            return false;

        return node.IsLogicalDriveNode || node.IsFolderNode;
    }

    private void Tree_PreviewMouseMove(object sender, MouseEventArgs e)
        => TryStartTreeDrag(e);

    private void TreeViewItem_MouseMove(object sender, MouseEventArgs e)
        => TryStartTreeDrag(e);

    private void TryStartTreeDrag(MouseEventArgs e)
    {
        if (!_treeDragPending || e.LeftButton is not MouseButtonState.Pressed)
            return;

        var delta = e.GetPosition(null) - _treeDragStart;
        if (delta.LengthSquared < DRAG_START_DISTANCE_SQUARED)
            return;

        var node = _treeDragNode;
        var item = _treeDragItem;
        if (node is null || item is null || !CanStartTreeDrag(node))
            return;

        _treeDragPending = false;
        _treeDidDrag = true;
        ReleaseTreeDragCapture();

        InitiateTreeDrag(item, node);
    }

    private void Tree_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => FinishPendingTreeClick();

    private void TreeViewItem_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        => FinishPendingTreeClick();

    private void FinishPendingTreeClick()
    {
        if (!_treeDragPending)
        {
            ReleaseTreeDragCapture();
            return;
        }

        var item = _treeDragItem;
        _treeDragPending = false;
        ReleaseTreeDragCapture();

        if (_treeDidDrag || item is null)
            return;

        item.IsSelected = true;
    }

    private void ReleaseTreeDragCapture()
    {
        if (_treeDragItem?.IsMouseCaptured == true)
            _treeDragItem.ReleaseMouseCapture();

        _treeDragItem = null;
        _treeDragNode = null;
    }

    private void InitiateTreeDrag(TreeViewItem item, NavigationTreeNode node)
    {
        var list = FileList.FromTreeNode(node);
        if (list?.SelectedFiles.Any() != true)
            return;

        var files = list.SelectedFiles.ToList();
        VirtualFileDataObject? vfdo;
        using (Data.Use(list))
        {
            vfdo = VirtualFileDataObject.PrepareTransfer(
                files,
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
            if (vfdo is null)
                return;

            Data.CopyPaste.UpdateSelfVFDO(true);
        }

        Data.CopyPaste.DragStatus = CopyPasteService.DragState.Active;
        Data.CopyPaste.WasDragging = true;
        Data.CopyPaste.DragBitmap = files[0].DragImage;
        node.IsDragSource = true;
        NavigationTreeNode.SuppressUserSelectFromExpander++;
        DragAutoScroll.Register(TreeScrollViewer);
        DragAutoScroll.Begin();
        try
        {
            vfdo.SendObjectToShell(
                VirtualFileDataObject.DataObjectMethod.DragDrop,
                item,
                vfdo.PreferredDropEffect ?? DragDropEffects.Copy);
        }
        finally
        {
            node.IsDragSource = false;
            Data.CopyPaste.DragStatus = CopyPasteService.DragState.None;
            if (Data.CopyPaste.IsDrag)
                Data.CopyPaste.ClearDrag();
            ClearTreeDropHighlight();
            if (NavigationTreeNode.SuppressUserSelectFromExpander > 0)
                NavigationTreeNode.SuppressUserSelectFromExpander--;

            DragAutoScroll.End();
        }
    }

    private void TreeViewItem_DragEnter(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem item || item.DataContext is not NavigationTreeNode node)
            return;

        SetTreeDropHighlight(node, Data.CopyPaste.GetAllowedTreeDropEffects(e.Data, node));
        ApplyTreeDropEffects(e, node);
        e.Handled = true;
    }

    private void TreeViewItem_DragOver(object sender, DragEventArgs e)
    {
        if (sender is not TreeViewItem item || item.DataContext is not NavigationTreeNode node)
            return;

        ApplyTreeDropEffects(e, node);
        e.Handled = true;
    }

    private void TreeViewItem_DragLeave(object sender, DragEventArgs e)
    {
        if (sender is TreeViewItem item && item.DataContext is NavigationTreeNode node)
            ClearTreeDropHighlight(node);

        e.Handled = true;
    }

    private void TreeViewItem_Drop(object sender, DragEventArgs e)
    {
        if (sender is TreeViewItem item && item.DataContext is NavigationTreeNode node)
        {
            ApplyTreeDropEffects(e, node);
            if (e.Effects is not DragDropEffects.None)
                Data.CopyPaste.AcceptTreeDrop(e, node);
        }

        ClearTreeDropHighlight();
        e.Handled = true;
    }

    private void ApplyTreeDropEffects(DragEventArgs e, NavigationTreeNode node)
    {
        var allowed = Data.CopyPaste.GetAllowedTreeDropEffects(e.Data, node);
        SetTreeDropHighlight(node, allowed);

        e.Effects = CopyPasteService.ResolveDropEffect(allowed, e.KeyStates, Data.CopyPaste.IsFromDevice(node.OwnerDevice));

        Data.CopyPaste.DropEffect =
        Data.CopyPaste.CurrentDropEffect = e.Effects;
        Data.CopyPaste.DropTarget = node.Path;
        Data.CopyPaste.DropTargetDevice = node.OwnerDevice;

        // Foreign drags (e.g. from Windows Explorer) never set a drag image on drag start like our own
        // drags do, so DragWindow stays hidden unless something sets one while dragging over a drop
        // target - mirrors ExplorerGrid_DragOver's equivalent handling for the explorer grid.
        var isAppDrive = node.Drive?.Type is AbstractDrive.DriveType.Package;
        if (isAppDrive)
        {
            if (!Data.CopyPaste.IsSelf && FileHelper.AllFilesAreApks(Data.CopyPaste.DragFiles))
                Data.CopyPaste.DragBitmap = DefaultAndroidPackageIcon.Bitmap;
        }
        else if (Data.CopyPaste.CurrentFiles.Any())
        {
            Data.CopyPaste.DragBitmap = Data.CopyPaste.CurrentFiles.First().DragImage;
        }
    }

    private void SetTreeDropHighlight(NavigationTreeNode node, DragDropEffects allowed)
    {
        if (_treeDropHighlight is not null && !ReferenceEquals(_treeDropHighlight, node))
            _treeDropHighlight.IsDragOver = false;

        _treeDropHighlight = node;
        node.IsDragOver = allowed is not DragDropEffects.None;
    }

    private void ClearTreeDropHighlight(NavigationTreeNode? node = null)
    {
        if (node is not null && !ReferenceEquals(_treeDropHighlight, node))
        {
            node.IsDragOver = false;
            return;
        }

        if (_treeDropHighlight is not null)
            _treeDropHighlight.IsDragOver = false;

        _treeDropHighlight = null;
    }
}
