using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    public static void IsPasteEnabled()
    {
        // Do not update if drag is active
        if (Data.CopyPaste.IsDrag)
            return;

        if (!Data.CopyPaste.HasFiles)
        {
            ActionFlags.CutItemsCount.Value = "";

            if (CanPasteClipboardImage())
            {
                SetClipboardImagePasteLabels(ActionFlags);
                if (!ReferenceEquals(ActionFlags, Data.FileActions))
                    SetClipboardImagePasteLabels(Data.FileActions);

                ActionFlags.PasteEnabled = true;
                ActionFlags.IsKeyboardPasteEnabled = true;
                return;
            }

            ResetPasteImageLabels(ActionFlags);
            if (!ReferenceEquals(ActionFlags, Data.FileActions))
                ResetPasteImageLabels(Data.FileActions);

            ActionFlags.PasteEnabled = false;
            ActionFlags.IsKeyboardPasteEnabled = false;
            return;
        }

        SetPasteLabels(ActionFlags);
        if (!ReferenceEquals(ActionFlags, Data.FileActions))
            SetPasteLabels(Data.FileActions);

        if (!ActionFlags.IsPasteStateVisible)
        {
            ActionFlags.PasteEnabled = false;
            ActionFlags.IsKeyboardPasteEnabled = false;
            return;
        }

        if (ActionFlags.IsAppDrive)
        {
            ActionFlags.PasteEnabled = FileHelper.AllFilesAreApks(Data.CopyPaste.Files);
            ActionFlags.IsKeyboardPasteEnabled = false;
        }
        else if (Data.CopyPaste.PasteState is DragDropEffects.Link)
        {
            ActionFlags.PasteEnabled = false;
            ActionFlags.IsKeyboardPasteEnabled = false;
        }
        else
        {
            ActionFlags.PasteEnabled = EnableUiPaste();
            ActionFlags.IsKeyboardPasteEnabled = EnableKeyboardPaste();
        }
    }

    // Each FileActionsEnable is its own tab's - a shared/cached BaseIcon here would have its one
    // WPF element pulled into whichever tab's toolbar last bound it, leaving the rest blank.
    private static void SetClipboardImagePasteLabels(FileActionsEnable actions)
    {
        actions.PasteDescription.Value = Strings.Resources.S_MENU_PASTE_IMAGE;
        actions.PasteIcon.Value = new BaseIcon(new ClipboardImageIcon(), 18);
        actions.ContextPasteIcon.Value = new BaseIcon(new ClipboardImageIcon(), 16);
    }

    private static void ResetPasteImageLabels(FileActionsEnable actions)
    {
        actions.PasteIcon.Value = new BaseIcon(new PasteIcon(), 18);
        actions.ContextPasteIcon.Value = new BaseIcon(new PasteIcon(), 16);
    }

    private static void SetPasteLabels(FileActionsEnable actions)
    {
        ResetPasteImageLabels(actions);

        actions.CutItemsCount.Value = Data.CopyPaste.Files.Length.ToString();

        if (Data.CopyPaste.Files.Length > 1)
        {
            if (actions.IsAppDrive)
            {
                actions.PasteDescription.Value = string.Format(
                    Strings.Resources.S_DRAG_INSTALL_MULTIPLE,
                    Data.CopyPaste.Files.Length);
            }
            else if (Data.CopyPaste.PasteState is DragDropEffects.Move)
            {
                actions.PasteDescription.Value = string.Format(
                    Strings.Resources.S_PASTE_PLURAL_CUT_ITEMS,
                    Data.CopyPaste.Files.Length);
            }
            else
            {
                actions.PasteDescription.Value = string.Format(
                    Strings.Resources.S_PASTE_PLURAL_COPIED_ITEMS,
                    Data.CopyPaste.Files.Length);
            }

            return;
        }

        if (actions.IsAppDrive)
        {
            actions.PasteDescription.Value = string.Format(
                Strings.Resources.S_DRAG_INSTALL_SINGLE,
                Data.CopyPaste.CurrentFiles.FirstOrDefault()?.NoExtName);
        }
        else if (Data.CopyPaste.PasteState is DragDropEffects.Move)
        {
            actions.PasteDescription.Value = Strings.Resources.S_PASTE_ONE_CUT_ITEM;
        }
        else
        {
            actions.PasteDescription.Value = Strings.Resources.S_PASTE_ONE_COPIED_ITEM;
        }
    }

    public static bool EnableUiPaste() => EnablePaste(keyboard: false);

    public static bool EnableKeyboardPaste() => EnablePaste(keyboard: true);

    private static bool EnablePaste(bool keyboard)
    {
        if (ActionFlags.IsRecycleBin || ActionList.ForbidPaste)
            return false;

        string[] files = Data.CopyPaste.Files;
        if (Data.CopyPaste.IsWindows
            && Data.CopyPaste.IsVirtual
            && Data.CopyPaste.Descriptors.Length == files.Length)
        {
            files = [.. Data.CopyPaste.Descriptors.Select(d => d.Name)];
        }

        ActionFlags.IsPastingInDescendant = AppliesAndroidSelfPasteRules()
            && files.Length == 1
            && FileHelper.RelationFrom(files[0], ActionPath) is RelationType.Descendant or RelationType.Self;

        if (ActionFlags.IsPastingInDescendant)
            return false;

        var selected = Data.SelectedFiles?.Count();
        if (keyboard && selected > 1)
            selected = 0;

        var deviceId = ActionDevice?.ID ?? "";

        string targetPath;
        if (selected == 1)
        {
            var targetFile = Data.SelectedFiles.First();
            var path = targetFile.IsLink ? targetFile.LinkTarget : targetFile.FullPath;
            targetPath = ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
        }
        else
        {
            targetPath = ActionPath;
        }

        if (!IsPasteIntoTargetAllowed(targetPath))
            return false;

        UpdatePastingRestrictions(targetPath, files);

        if (ActionFlags.IsPastingIllegalNaming || ActionFlags.IsPastingConflictingNames)
            return false;

        var sameDevice = AppliesAndroidSelfPasteRules();
        switch (selected)
        {
            case 0:
                ActionFlags.IsPastingInDescendant = sameDevice
                    && NavigationTreeNode.PathsEqual(Data.CopyPaste.ParentFolder, ActionPath)
                    && Data.CopyPaste.PasteState is DragDropEffects.Move;

                break;
            case 1:
                // When duplicating a file multiple times using the keyboard, the selection is the previous copy
                if (keyboard
                    && Data.CopyPaste.PasteState is DragDropEffects.Copy
                    && Data.DirList?.FileList.Any(f => f.FullPath == files[0]) is true)
                    return DriveHelper.IsModificationAllowedAt(targetPath, deviceId);

                var item = Data.SelectedFiles.First();
                if (!ArchiveHelper.IsPasteTargetContainer(item, deviceId))
                    return false;

                ActionFlags.IsPastingInDescendant = sameDevice
                    && ((files.Length == 1 && NavigationTreeNode.PathsEqual(files[0], item.FullPath))
                        || NavigationTreeNode.PathsEqual(Data.CopyPaste.ParentFolder, item.FullPath));

                break;
            default:
                return false;
        }

        return !ActionFlags.IsPastingInDescendant
            && DriveHelper.IsModificationAllowedAt(targetPath, deviceId);
    }

    /// <summary>
    /// Path-only "paste into self/descendant" applies only when the clipboard is from
    /// the same Android device as the paste target. The same path on another device is a different folder.
    /// </summary>
    private static bool AppliesAndroidSelfPasteRules()
    {
        if (!Data.CopyPaste.CurrentSource.HasFlag(CopyPasteService.DataSource.Android)
            && !Data.CopyPaste.IsSelf)
            return false;

        return Data.CopyPaste.IsFromDevice(ActionDevice);
    }

    private static bool IsPasteIntoTargetAllowed(string targetPath)
    {
        if (ActionFlags.IsSearchMode && FileHelper.IsSearchLocation(targetPath))
            return false;

        var deviceId = ActionDevice?.ID ?? "";
        if (!ArchivePath.IsArchivePath(targetPath, deviceId))
            return true;

        return ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId);
    }

    private static bool IsSymlinkPasteAllowed(string targetPath)
    {
        var deviceId = Data.ActiveDevice?.ID ?? "";
        var restrictions = DriveHelper.GetRestrictions(targetPath);
        return HasRootShell
            && Data.CopyPaste.Files.Length == 1
            && Data.CopyPaste.IsSelf
            && restrictions.NoSymbolicLinks is not true
            && restrictions.ReadOnly is not true
            && !ArchivePath.IsArchivePath(targetPath, deviceId);
    }

    public static DragDropEffects EnableDropPaste(FileClass? target = null)
    {
        if (!Data.CopyPaste.CurrentFiles.Any())
            return DragDropEffects.None;

        var pastingInDescendant = Data.CopyPaste.DragFiles.Length == 1
            && Data.CopyPaste.CurrentFiles.First().Relation(Data.CurrentPath) is RelationType.Descendant or RelationType.Self;

        if (pastingInDescendant || Data.FileActions.IsRecycleBin)
            return DragDropEffects.None;

        if (target is null && Data.FileActions.IsSearchMode)
            return DragDropEffects.None;

        if (FileHelper.RelationFrom(Data.CopyPaste.DragParent, AdbExplorerConst.RECYCLE_PATH) is RelationType.Self or RelationType.Ancestor)
            return DragDropEffects.Move;

        string targetPath = target switch
        {
            null => Data.CurrentPath,
            _ when target.IsLink => target.LinkTarget,
            _ => target.FullPath,
        };

        var deviceId = Data.ActiveDevice?.ID ?? "";
        targetPath = ArchiveHelper.ResolvePasteTargetPath(targetPath, deviceId);

        if (!DriveHelper.IsModificationAllowedAt(targetPath, deviceId))
            return DragDropEffects.None;

        var intoArchive = ArchivePath.IsArchivePath(targetPath, deviceId);
        if (intoArchive && !ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
            return DragDropEffects.None;

        UpdatePastingRestrictions(targetPath, [.. Data.CopyPaste.CurrentFiles.Select(f => f.FullPath)]);

        var fromArchive = ArchiveExtract.IsArchiveSource(Data.CopyPaste.CurrentFiles, deviceId);
        // Archive → archive not supported.
        if (intoArchive && fromArchive)
            return DragDropEffects.None;

        var result = DragDropEffects.Copy;
        // Link and archive targets are incompatible; archive extract is copy-only for sources.
        if (!intoArchive
            && !fromArchive
            && HasRootShell
            && Data.CopyPaste.IsSelf
            && DriveHelper.GetRestrictions(targetPath).NoSymbolicLinks is not true
            && Data.CopyPaste.CurrentFiles.Count() == 1)
            result |= DragDropEffects.Link;

        if (Data.FileActions.IsPastingIllegalNaming || Data.FileActions.IsPastingConflictingNames)
            return DragDropEffects.None;

        if (target is null)
        {
            if (Data.CopyPaste.DragParent == Data.CurrentPath)
                return result;
        }
        else
        {
            if (!ArchiveHelper.IsPasteTargetContainer(target, deviceId))
                return DragDropEffects.None;

            pastingInDescendant = (Data.CopyPaste.DragFiles.Length == 1 && Data.CopyPaste.CurrentFiles.First().FullPath == target.FullPath)
                || (Data.CopyPaste.DragParent == target.FullPath);
        }

        if (pastingInDescendant)
            return DragDropEffects.None;

        // Archive extract is copy-only.
        return fromArchive ? result : result | DragDropEffects.Move;
    }

    public static DragDropEffects EnableTreeDropPaste(NavigationTreeNode target)
    {
        if (!Data.CopyPaste.CurrentFiles.Any())
            return DragDropEffects.None;

        if (target.Device is not null || target.IsTemp || target.IsInEditMode)
            return DragDropEffects.None;

        var drive = target.Drive ?? DriveHelper.GetCurrentDrive(target.Path, target.OwnerDevice);
        if (drive is null)
            return DragDropEffects.None;

        if (drive.Type is AbstractDrive.DriveType.Trash)
            return DragDropEffects.None;

        if (target.Drive?.Type is AbstractDrive.DriveType.Root)
            return DragDropEffects.None;

        var deviceId = target.OwnerDevice?.ID ?? Data.ActiveDevice?.ID ?? "";

        if (drive.Type is AbstractDrive.DriveType.Package)
        {
            if (Data.CopyPaste.IsSelf && Data.FileActions.IsAppDrive)
                return DragDropEffects.None;

            return FileHelper.AllFilesAreApks(Data.CopyPaste.DragFiles)
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        }

        var targetPath = ArchiveHelper.ResolvePasteTargetPath(target.DropTargetPath, deviceId);
        if (!DriveHelper.IsModificationAllowedAt(targetPath, deviceId))
            return DragDropEffects.None;

        if (ArchivePath.IsArchivePath(targetPath, deviceId)
            && !ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
            return DragDropEffects.None;

        if (FileHelper.RelationFrom(Data.CopyPaste.DragParent, AdbExplorerConst.RECYCLE_PATH)
            is RelationType.Self or RelationType.Ancestor)
            return DragDropEffects.Move;

        UpdatePastingRestrictions(targetPath, [.. Data.CopyPaste.CurrentFiles.Select(f => f.FullPath)]);
        if (Data.FileActions.IsPastingIllegalNaming || Data.FileActions.IsPastingConflictingNames)
            return DragDropEffects.None;

        var fromArchive = ArchiveExtract.IsArchiveSource(Data.CopyPaste.CurrentFiles, deviceId);
        var intoArchive = ArchivePath.IsArchivePath(targetPath, deviceId);
        if (intoArchive && fromArchive)
            return DragDropEffects.None;

        if (Data.CopyPaste.CurrentSource.HasFlag(CopyPasteService.DataSource.Android))
        {
            var sameDevice = Data.CopyPaste.IsFromDevice(target.OwnerDevice);
            if (sameDevice)
            {
                foreach (var file in Data.CopyPaste.CurrentFiles)
                {
                    var relation = FileHelper.RelationFrom(file.FullPath, targetPath);
                    if (relation is RelationType.Self or RelationType.Descendant)
                        return DragDropEffects.None;
                }
            }

            var result = DragDropEffects.Copy;
            if (!intoArchive
                && !fromArchive
                && target.OwnerDevice?.HasRootShell == true
                && sameDevice
                && DriveHelper.GetRestrictions(targetPath, target.OwnerDevice).NoSymbolicLinks is not true
                && Data.CopyPaste.CurrentFiles.Count() == 1)
                result |= DragDropEffects.Link;

            return fromArchive ? result : result | DragDropEffects.Move;
        }

        if (Data.CopyPaste.CurrentSource.HasFlag(CopyPasteService.DataSource.Virtual))
            return DragDropEffects.Copy;

        if (ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
            return DragDropEffects.Copy | DragDropEffects.Move;

        return DragDropEffects.Move | DragDropEffects.Copy;
    }

    private static void UpdatePastingRestrictions(string targetPath, string[] files)
    {
        var restrictions = DriveHelper.GetRestrictions(targetPath);

        if (ActionFlags.IsAppDrive)
        {
            ActionFlags.IsPastingIllegalNaming = Data.CopyPaste.IsSelf
                && DriveHelper.GetRestrictions(files[0]).RestrictedNaming;
            return;
        }

        ActionFlags.IsPastingIllegalNaming = restrictions.RestrictedNaming
            && !FileHelper.FileNameLegal(files.Select(FileHelper.GetFullName), FileHelper.RenameTarget.RestrictedNaming);

        ActionFlags.IsPastingConflictingNames = restrictions.CaseInsensitiveNames
            && files.Distinct(StringComparer.InvariantCultureIgnoreCase).Count() != files.Length;
    }

    public static void PasteFiles(IEnumerable<FileClass> selectedFiles, bool isLink = false)
    {
        // Toolbar and keyboard paste share this executor - CanExecute already applied the
        // stricter (non-keyboard) selection rule for toolbar/context, so a multi-selection
        // can only reach here via the keyboard's more lenient gating.
        if (!Data.CopyPaste.HasFiles && CanPasteClipboardImageAtSelection(isKeyboard: true))
        {
            BeginPasteClipboardImage();
            return;
        }

        Data.CopyPaste.AcceptDataObject(Clipboard.GetDataObject(), selectedFiles, isLink);

        IsPasteEnabled();
    }

    public static void CutItems(bool isCopy = false)
    {
        if (ActionFlags.IsAppDrive)
            CopyPackages(Data.SelectedPackages);
        else
            CutFiles(Data.SelectedFiles, isCopy);
    }

    public static void CutFiles(IEnumerable<FileClass> items, bool isCopy = false)
    {
        var itemsToCut = HasRootShell
                    ? items : items.Where(file => file.Type is FileType.File or FileType.Folder);

        ActionFlags.CopyEnabled = !isCopy;
        ActionFlags.CutEnabled = isCopy;

        IsPasteEnabled();

        var dropEffect = isCopy ? DragDropEffects.Copy : DragDropEffects.Move;
        var vfdo = VirtualFileDataObject.PrepareTransfer(itemsToCut, dropEffect, VirtualFileDataObject.DataObjectMethod.Clipboard);
        if (vfdo is null)
            return;

        // Mark clipboard as self immediately so paste enablement works while descriptors
        // (and archive extract staging) finish asynchronously.
        Data.CopyPaste.UpdateSelfVFDO(isDrag: false, pasteEffect: dropEffect);
        vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.Clipboard, allowedEffects: dropEffect);
        Data.CopyPaste.MarkSelfClipboardWritten();
    }

    public static void CopyLinkFiles(IEnumerable<FileClass> items)
    {
        var itemsToCopy = items;

        ActionFlags.CopyEnabled = true;
        ActionFlags.CutEnabled = true;

        IsPasteEnabled();

        var dropEffect = DragDropEffects.Link;
        var vfdo = VirtualFileDataObject.PrepareTransfer(itemsToCopy, dropEffect, VirtualFileDataObject.DataObjectMethod.Clipboard);
        if (vfdo is null)
            return;

        Data.CopyPaste.UpdateSelfVFDO(isDrag: false, pasteEffect: dropEffect);
        vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.Clipboard, allowedEffects: dropEffect);
        Data.CopyPaste.MarkSelfClipboardWritten();
    }

    private static string GetUiPasteTargetPath()
    {
        if (Data.SelectedFiles?.Count() == 1
            && Data.SelectedFiles.First() is { } item
            && ActionDevice is { } device
            && ArchiveHelper.IsPasteTargetContainer(item, device.ID))
        {
            var path = item.IsLink ? item.LinkTarget : item.FullPath;
            return ArchiveHelper.ResolvePasteTargetPath(path, device.ID);
        }

        return ActionPath;
    }
}
