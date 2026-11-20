namespace ADB_Explorer.Services;

/// <summary>
/// A drag of a navigation tree node's folder or drive, as files of our own - from the tree, or from the drive view's
/// card standing for a drive's node.
/// </summary>
public static class NodeDrag
{
    /// <summary>Whether the node's folder or drive can be dragged now.</summary>
    public static bool CanDrag(NavigationTreeNode node)
    {
        if (App.Services.GetService<ExplorerViewModel>()?.Tree?.IsTreeDragBlocked != false)
            return false;

        if (node.IsTemp || node.IsInEditMode)
            return false;

        return node.IsLogicalDriveNode || node.IsFolderNode;
    }

    /// <summary>Drags the node's folder or drive until the drag ends.</summary>
    public static void Run(UIElement source, NavigationTreeNode node)
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

        // A drive is shown by its own icon and name, not its root folder's
        Data.CopyPaste.SelfDragDrive = node.Drive;
        Data.CopyPaste.DragBitmap = node.Drive?.DragImage ?? files[0].DragImage;

        try
        {
            vfdo.SendObjectToShell(
                VirtualFileDataObject.DataObjectMethod.DragDrop,
                source,
                DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link);
        }
        finally
        {
            Data.CopyPaste.SelfDragDrive = null;
            Data.CopyPaste.DragStatus = CopyPasteService.DragState.None;
            if (Data.CopyPaste.IsDrag)
                Data.CopyPaste.ClearDrag();
        }
    }
}
