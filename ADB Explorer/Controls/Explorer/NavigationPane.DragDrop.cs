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

        // A device node opens the device's mount where it's dropped, outside the app
        if (node?.Device is { } device && item is not null && !node.IsInEditMode && DeviceMountDrag.CanDrag(device))
        {
            _treeDragPending = false;
            _treeDidDrag = true;
            ReleaseTreeDragCapture();

            InitiateDeviceDrag(item, device);
            return;
        }

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

    /// <summary>Drags the device by its node's row, held where it was grabbed.</summary>
    private void InitiateDeviceDrag(TreeViewItem item, LogicalDeviceViewModel device)
    {
        Point? grabPoint = null;
        if (Window.GetWindow(item) is { } window && item.Template.FindName("Border", item) is FrameworkElement row)
            grabPoint = window.TranslatePoint(_treeDragStart, row);

        DeviceMountDrag.Start(item, device, grabPoint);
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
            // No preferred effect: Explorer then defaults to Copy, while a preferred Copy alone makes it refuse Shift's Move
            vfdo = VirtualFileDataObject.PrepareTransfer(files, DragDropEffects.None);
            if (vfdo is null)
                return;

            Data.CopyPaste.UpdateSelfVFDO(true);
        }

        Data.CopyPaste.BeginSelfDrag();
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
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
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
        => SetTreeDropHighlight(node, Data.CopyPaste.ApplyTreeDropEffects(e, node));

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
