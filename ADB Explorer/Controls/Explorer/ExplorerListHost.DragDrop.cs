using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Controls;

public partial class ExplorerListHost
{
    private void DataGridRow_Drop(object sender, DragEventArgs e)
    {
        // The pane under the mouse is the drop location, whichever pane is focused.
        using (Data.UseInstance(Owner.Instance))
            CopyPaste.AcceptDataObject(e, (FrameworkElement)sender);

        e.Handled = true;
    }

    private void Row_PreviewDragEnter(object sender, DragEventArgs e)
    {
        // Nothing in the recycle bin is a drop target, nor is a folder for a dragged device, tab or page
        if (((FrameworkElement)sender).DataContext is FileClass file && !FileHelper.IsInTrash(file.FullPath) && !CopyPaste.IsContentDrag)
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
        // The pane under the mouse is the drop location, whichever pane is focused.
        using (Data.UseInstance(Owner.Instance))
            DragOverCore(sender, e);
    }

    private void DragOverCore(object sender, DragEventArgs e)
    {
        // The recycle bin never accepts drops, so there's no drag data to read or target to resolve
        if (FileHelper.IsInTrash(Data.CurrentPath))
        {
            e.Effects = DragDropEffects.None;
            CopyPaste.DropEffect =
            CopyPaste.CurrentDropEffect = DragDropEffects.None;
            e.Handled = true;
            return;
        }

        var allowed = CopyPaste.GetAllowedDragEffects(e.Data, (FrameworkElement)sender);

        e.Effects = CopyPasteService.ResolveDropEffect(allowed, e.KeyStates, CopyPaste.IsSelf);

        CopyPaste.DropEffect =
        CopyPaste.CurrentDropEffect = e.Effects;

        // A drag cancelled with a right click stays without its drag window
        if (CopyPaste.IsDragCanceled)
        {
            e.Handled = true;
            return;
        }

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
        else if (CopyPaste.FilesDragImage() is { } image)
        {
            CopyPaste.DragBitmap = image;
        }

        e.Handled = true;
    }

    private void ClearWasDraggingAfterContext()
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (CopyPaste.DragStatus is not CopyPasteService.DragState.Active)
                ClearWasDraggingIfIdle();
        }, DispatcherPriority.Input);
    }

    /// <summary>
    /// A drag cancelled with the right button leaves the left one held, and both releases must still
    /// be swallowed, in either order - so the flag stays until no button is down.
    /// </summary>
    internal void ClearWasDraggingIfIdle()
    {
        if (MouseState.IsAnyButtonDown)
            return;

        CopyPaste.WasDragging = false;
    }

    private void InitiateDrag(DependencyObject dragSource)
    {
        IEnumerable<FileClass> selectedItems;
        VirtualFileDataObject? vfdo;
        DragDropEffects allowedEffects;
        if (FileActions.IsAppDrive)
        {
            vfdo = VirtualFileDataObject.PrepareTransfer(ActiveSelectedItems.Cast<Package>());
            selectedItems = VirtualFileDataObject.SelfFiles!;
            allowedEffects = DragDropEffects.Copy;
        }
        else
        {
            selectedItems = ActiveSelectedItems.Cast<FileClass>();
            // Archive extract is copy-only (no cut / symlink from inside an archive).
            allowedEffects = FileActions.IsArchive
                ? DragDropEffects.Copy
                : DragDropEffects.Copy | DragDropEffects.Move | DragDropEffects.Link;

            // No preferred effect: Explorer then defaults to Copy, while a preferred Copy alone makes it refuse Shift's Move
            vfdo = VirtualFileDataObject.PrepareTransfer(selectedItems, DragDropEffects.None);
        }

        if (vfdo is null)
            return;

        CopyPaste.BeginSelfDrag();
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
            vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.DragDrop, dragSource, allowedEffects);
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
}
