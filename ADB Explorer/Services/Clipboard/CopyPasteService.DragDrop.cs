using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

public partial class CopyPasteService
{
    /// <summary>
    /// Picks the drop effect from the allowed effects and modifier keys: move within the same source, copy otherwise.
    /// </summary>
    public static DragDropEffects ResolveDropEffect(DragDropEffects allowed, DragDropKeyStates keys, bool sameSource)
    {
        if ((!allowed.HasFlag(DragDropEffects.Copy) && keys.HasFlag(DragDropKeyStates.ControlKey))
            || (!allowed.HasFlag(DragDropEffects.Move) && keys.HasFlag(DragDropKeyStates.ShiftKey))
            || (!allowed.HasFlag(DragDropEffects.Link) && keys.HasFlag(DragDropKeyStates.AltKey)))
        {
            return DragDropEffects.None;
        }

        if (allowed.HasFlag(DragDropEffects.Move)
            && sameSource
            && !keys.HasFlag(DragDropKeyStates.ControlKey)
            && !keys.HasFlag(DragDropKeyStates.AltKey))
        {
            return DragDropEffects.Move;
        }

        if (allowed.HasFlag(DragDropEffects.Move) && keys.HasFlag(DragDropKeyStates.ShiftKey))
            return DragDropEffects.Move;

        if (allowed.HasFlag(DragDropEffects.Link) && keys.HasFlag(DragDropKeyStates.AltKey))
            return DragDropEffects.Link;

        // Copy is the default and does not require Ctrl to be activated
        if (allowed.HasFlag(DragDropEffects.Copy))
            return DragDropEffects.Copy;

        return allowed;
    }

    public DragDropEffects GetAllowedDragEffects(IDataObject dataObject, FrameworkElement? sender = null)
    {
        if (sender is null)
        {
            PasteSource &= ~DataSource.None;
            DragPasteSource = DataSource.None;
        }
        else
        {
            DragPasteSource &= ~DataSource.None;
            if (TrackDrag(dataObject))
                return DragDropEffects.None;
        }

        PreviewDataObject(dataObject);
        if (DragFiles.Length < 1)
            return DragDropEffects.None;

        // Clipboard evaluation (no drop target): keep the paste payload even when the
        // current location cannot accept it (e.g. still inside a read-only archive).
        // EnableUiPaste / EnableKeyboardPaste re-check the real destination on navigate.
        if (sender is null)
        {
            if (Data.FileActions.IsAppDrive)
            {
                if (IsSelf)
                    return DragDropEffects.None;

                return FileHelper.AllFilesAreApks(DragFiles) ? DragDropEffects.Copy : DragDropEffects.None;
            }

            if (CurrentSource.HasFlag(DataSource.Virtual) && !CurrentSource.HasFlag(DataSource.Android))
                return DragDropEffects.Copy;

            return DragDropEffects.Move | DragDropEffects.Copy;
        }

        var dataContext = sender.DataContext;
        FileClass file = dataContext is FileClass fc ? fc : null;
        Data.CopyPaste.DropTargetDevice = Data.ActiveDevice;

        if (Data.FileActions.IsAppDrive)
        {
            // App drive drag is pull-to-elsewhere only — never install/move onto itself.
            if (IsSelf)
                return DragDropEffects.None;

            if (FileHelper.AllFilesAreApks(DragFiles))
                return DragDropEffects.Copy;
        }
        else if (file is null
            || file.IsDirectory
            || ArchiveHelper.CanPasteIntoArchiveFile(file.IsLink ? file.LinkTarget : file.FullPath, Data.ActiveDevice?.ID ?? ""))
        {
            string rawPath;
            if (file is null)
                rawPath = Data.CurrentPath;
            else
                rawPath = file.IsLink ? file.LinkTarget : file.FullPath;

            var deviceId = Data.ActiveDevice?.ID ?? "";
            var targetPath = ArchiveHelper.ResolvePasteTargetPath(rawPath, deviceId);
            Data.CopyPaste.DropTarget = targetPath;

            if (!DriveHelper.IsModificationAllowedAt(targetPath, deviceId))
                return DragDropEffects.None;

            if (ArchivePath.IsArchivePath(targetPath, deviceId)
                && !ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
                return DragDropEffects.None;

            if (CurrentSource.HasFlag(DataSource.Android))
            {
                if (IsDrag)
                    return FileActionLogic.EnableDropPaste(file);
            }
            else if (CurrentSource.HasFlag(DataSource.Virtual))
                return DragDropEffects.Copy;
            
            // Windows filesystem drop: Copy|Move into tar, never Link.
            if (ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
                return DragDropEffects.Copy | DragDropEffects.Move;

            return DragDropEffects.Move | DragDropEffects.Copy;
        }

        return DragDropEffects.None;
    }

    public void PreviewDataObject(IDataObject dataObject)
    {
        CurrentSource &= ~(DataSource.Android | DataSource.Virtual);
        IsFileDropOnly = IsDelayedFileDrop = HasUnnamedFiles = HasPendingFiles = false;
        ForgetOtherDragThumbnail(dataObject);

        DragParent = "";
        string[] oldFiles = [.. DragFiles];

        // ADB Drop - for all Android to Android transfers (including self)
        if (dataObject.GetDataPresent(AdbDataFormats.AdbDrop) && dataObject.GetData(AdbDataFormats.AdbDrop) is MemoryStream adbStream)
        {
            var dragList = NativeMethods.ADBDRAGLIST.FromStream(adbStream);
            var deviceId = dragList.deviceId;

            var device = Data.DevicesObject.UIList.OfType<LogicalDeviceViewModel>().FirstOrDefault(d => d.Matches(deviceId) && d.Status is DeviceStatus.Ok);
            if (device is null && SourceDevice?.ID == deviceId)
                device = SourceDevice;

            if (!IsDrag && device is null)
            {
                Clear();
                return;
            }

            SourceDevice = device;

            DragParent = dragList.parentFolder;
            DragFiles = [.. dragList.items.Select(f => FileHelper.ConcatPaths(DragParent, f))];

            CurrentSource |= DataSource.Android;

            if (dataObject.GetDataPresent(AdbDataFormats.FileDescriptor))
            {
                // Descriptors are filled asynchronously (and for archives only after extract-to-tmp).
                // Retry until ready; empty placeholder bytes must not be treated as a real group.
                Task.Run(async () =>
                {
                    for (var attempt = 0; attempt < 40; attempt++)
                    {
                        await Task.Delay(250).ConfigureAwait(false);

                        FileDescriptor[]? fds = null;
                        App.SafeInvoke(() => fds = FileDescriptor.GetDescriptors(dataObject));
                        if (fds is not { Length: > 0 })
                            continue;

                        App.SafeInvoke(() =>
                        {
                            Descriptors = fds;
                            UpdateUI();
                        });
                        return;
                    }
                });
            }
        }
        // Shell ID List - the only format Microsoft supports for anything added after Windows XP (non-ZIP archives, UNC paths, etc.)
        else if (dataObject.GetDataPresent(AdbDataFormats.ShellidList))
        {
            SourceDevice = null;
            var ido = (System.Runtime.InteropServices.ComTypes.IDataObject)dataObject;
            ShellItemArray? shItems = null;

            try
            {
                shItems = ShellItemArray.FromDataObject(ido);
            }
            catch (Exception e) // E_ACCESS_DENIED may be thrown if the data object has already been disposed by the source, but not from the clipboard
            {
#if !DEPLOY
                DebugLog.PrintLine($"Failed to get ShellItemArray from IDataObject: {e}");
#endif
            }

            if (shItems is not null)
            {
                Descriptors = [.. shItems.Select(sh => new FileDescriptor(sh))];
                DragFiles = [.. shItems.Select(sh => sh.ParsingName)];

                CurrentSource &= ~DataSource.Android;
                if (!shItems[0].IsFileSystem)
                    CurrentSource |= DataSource.Virtual;
            }
        }
        // VFDO (FileGroupDescriptor + FileContents) - the only viable format for virtual files not mapped to a drive.
        // This is the format we supply to File Explorer. Also provided by File Explorer for contents of ZIP archives (introduced in Windows ME).
        // Not the new Outlook's, which is only a thumbnail of what it drags; its files come through FileDrop
        else if (dataObject.GetDataPresent(AdbDataFormats.FileDescriptor) && !IsOutlookDescribedDrag(dataObject))
        {
            SourceDevice = null;
            GetDescriptors(dataObject);

            DragFiles = [.. Descriptors.Where(d => !d.Name.Contains('\\') && !FileHelper.HasParentReference(d.Name)).Select(d => d.Name)];

            CurrentSource |= DataSource.Virtual;
            if (dataObject.GetDataPresent(AdbDataFormats.FileContents))
                CurrentSource &= ~DataSource.Android;
        }
        // FileDrop alone: an app with no shell namespace of its own (7-Zip, WinRAR, the new Outlook).
        // Otherwise it's probably dropping by target detect, which we can't support
        else if (!PreviewFileDrop(dataObject))
        {
            SourceDevice = null;
            DragFiles = [];
            UpdateUI();
        }

        if (oldFiles != DragFiles && IsDrag)
            UpdateUI();
    }

    public void GetDescriptors(IDataObject dataObject)
    {
        var fds = FileDescriptor.GetDescriptors(dataObject);
        if (fds is not null)
        {
            Descriptors = fds;
            UpdateUI();
        }
    }

    public void AcceptDataObject(System.Windows.DragEventArgs e, FrameworkElement sender)
    {
        var dataContext = sender.DataContext;
        var deviceId = Data.ActiveDevice?.ID ?? "";

        string targetFolder;
        if (dataContext is FileClass file && ArchiveHelper.IsPasteTargetContainer(file, deviceId))
        {
            var path = file.IsLink ? file.LinkTarget : file.FullPath;
            targetFolder = ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
        }
        else
            targetFolder = Data.CurrentPath;

        if (Data.FileActions.IsSearchMode
            && !(dataContext is FileClass dropTarget && ArchiveHelper.IsPasteTargetContainer(dropTarget, deviceId))
            && FileHelper.IsSearchLocation(targetFolder))
            return;
        
        // Do not perform implicit duplicate by drag (only with Ctrl)
        if (IsSelf && targetFolder == DragParent && e.KeyStates is DragDropKeyStates.None)
            return;

        AcceptDataObject(e.Data, targetFolder, e.KeyStates.HasFlag(DragDropKeyStates.AltKey));
    }

    public void AcceptDataObject(IDataObject dataObject, IEnumerable<FileClass> selectedFiles, bool isLink = false)
    {
        // Reads Data.Active, not the globally-open device: a tree context-menu paste scopes
        // Data.Active to the right-clicked node's own FileList (see Data.Use()), so this must
        // resolve to that target device rather than always pasting back onto the source device.
        var device = Data.Active.Device ?? Data.ActiveDevice;
        var deviceId = device?.ID ?? "";
        string targetFolder;
        if (selectedFiles.Count() == 1
            && selectedFiles.First() is { } item
            && ArchiveHelper.IsPasteTargetContainer(item, deviceId))
        {
            var path = item.IsLink ? item.LinkTarget : item.FullPath;
            targetFolder = ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
        }
        else
            targetFolder = Data.Active.Path;

        AcceptDataObject(dataObject, targetFolder, isLink, device, Data.Active.Actions.IsAppDrive);
    }

    public DragDropEffects GetAllowedTreeDropEffects(IDataObject dataObject, NavigationTreeNode node)
    {
        // Marks this as an active drag (routes CurrentSource through DragPasteSource instead of the
        // clipboard's PasteSource) so IsWindows/IsVirtual/IsSelf reflect the data actually being dragged,
        // not whatever was last copied. A self-initiated tree drag already did this via UpdateSelfVFDO,
        // but an external Explorer drag never goes through that, so it must happen here too.
        DragPasteSource &= ~DataSource.None;
        DropTargetDevice = node.OwnerDevice ?? Data.ActiveDevice;
        if (TrackDrag(dataObject))
            return DragDropEffects.None;

        PreviewDataObject(dataObject);
        if (DragFiles.Length < 1)
            return DragDropEffects.None;

        return FileActionLogic.EnableTreeDropPaste(node);
    }

    public void AcceptTreeDrop(System.Windows.DragEventArgs e, NavigationTreeNode node)
    {
        var allowed = GetAllowedTreeDropEffects(e.Data, node);
        if (allowed is DragDropEffects.None)
            return;

        var device = node.OwnerDevice ?? Data.ActiveDevice;
        var deviceId = device?.ID ?? "";
        var targetFolder = ArchiveHelper.ResolvePasteTargetPath(node.DropTargetPath, deviceId);
        var isAppDrive = node.Drive?.Type is AbstractDrive.DriveType.Package;

        if (IsFromDevice(device) && targetFolder == DragParent && e.KeyStates is DragDropKeyStates.None)
            return;

        AcceptDataObject(e.Data, targetFolder, e.KeyStates.HasFlag(DragDropKeyStates.AltKey), device, isAppDrive);
    }

    public void AcceptDataObject(IDataObject dataObject, string targetFolder, bool isLink = false)
        => AcceptDataObject(dataObject, targetFolder, isLink, Data.ActiveDevice, Data.FileActions.IsAppDrive);

    public void AcceptDataObject(IDataObject dataObject, string targetFolder, bool isLink, LogicalDeviceViewModel? device, bool isAppDrive)
    {
        var deviceId = device?.ID ?? "";
        if (device is null || FileHelper.IsSearchLocation(targetFolder))
            return;

        // Packages pulled from app drive are not dropped back onto it.
        if (isAppDrive && IsSelf && Data.FileActions.IsAppDrive)
            return;

        if (!DriveHelper.IsModificationAllowedAt(targetFolder, deviceId) && !isAppDrive)
            return;

        // Symlink into archives is not supported.
        if (isLink && ArchivePath.IsArchivePath(targetFolder, deviceId))
            return;

        if ((isLink || CurrentEffect is DragDropEffects.Link)
            && DriveHelper.GetRestrictions(targetFolder, device).NoSymbolicLinks)
            return;

        void ReadObject()
        {
            var fromOtherAndroid = CurrentSource.HasFlag(DataSource.Android)
                && SourceDevice is not null
                && SourceDevice.ID != device.ID;

            if (fromOtherAndroid && !isAppDrive)
            {
                VerifyAndTransfer(targetFolder, [.. CurrentFiles], CurrentEffect is DragDropEffects.Move, SourceDevice!, device);
            }
            // Virtual payload, or Android files that are not already on the drop target.
            // Self+Android with no explorer device is not Virtual, but is still a cross-device copy.
            else if (fromOtherAndroid || (IsVirtual && SourceDevice?.ID != device.ID))
            {
                ClearTempFolder();

                // Transfer from another Android device
                if (fromOtherAndroid || !IsWindows)
                {
                    foreach (var item in CurrentFiles)
                    {
                        SyncFile target = new(item) { PathType = FilePathType.Windows };
                        target.UpdatePath(FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, item.FullName, '\\'));

                        FolderTree[]? children = null;
                        if (item.IsDirectory)
                            children = item.GetChildren(SourceDevice!.ID);

                        // Pull the file from the source device to the temp folder
                        var pullOp = FileSyncOperation.PullFile(new(item, children), target, SourceDevice!, App.AppDispatcher);
                        pullOp.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName != nameof(FileSyncOperation.Status)
                                || pullOp.Status is not FileOperation.OperationStatus.Completed)
                                return;

                            // Once done, create a shell item and push it to the target device (current)
                            FileClass file = new(target) { ShellItem = ShellItem.Open(target.FullPath) };
                            if (isAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(device, [file.ShellItem], App.AppDispatcher);

                                return;
                            }

                            var pushOp = VerifyAndPush(targetFolder, file, CurrentEffect, device: device);
                            if (pushOp is null || CurrentEffect is not DragDropEffects.Move)
                                return;

                            pushOp.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName != nameof(FileSyncOperation.Status)
                                    || pushOp.Status is not FileOperation.OperationStatus.Completed)
                                    return;

                                // Once the second part is done, delete the file from the source device if needed
                                ShellFileOperation.SilentDelete(SourceDevice!, item.FullName);
                            };
                        };

                        Data.FileOpQ.AddOperation(pullOp);
                    }
                }
                // Files the source creates after the drop (the new Outlook); with no names, they can't be packages
                else if (IsDelayedFileDrop)
                {
                    if (!isAppDrive)
                    {
                        // The new Outlook's thumbnail is of the first attachment it describes
                        (string, BitmapSource)? thumbnail = null;
                        if (_pendingAreFiles && DragThumbnail is { } image && DragFiles.Length > 0)
                            thumbnail = (DragFiles[0], image);

                        PushDelayedFileDrop(dataObject, targetFolder, device, thumbnail);
                    }
                }
                // From archives, phones over MTP & DLNA servers, read straight from the shell.
                // The source stays where it is, as it did when this went through the temp folder.
                else if (dataObject.GetDataPresent(AdbDataFormats.ShellidList) && !isAppDrive)
                {
                    var shItems = ShellItemArray.FromDataObject((System.Runtime.InteropServices.ComTypes.IDataObject)dataObject);
                    VerifyAndPush(targetFolder, [.. shItems.Select(sh => new FileClass(sh))], DragDropEffects.Copy, device);
                }
                // Packages are installed from files on disk, so they go through the temp folder
                else if (dataObject.GetDataPresent(AdbDataFormats.ShellidList))
                {
                    ShellFolder tempDrag = new(Data.RuntimeSettings.TempDragPath);
                    var shItems = ShellItemArray.FromDataObject((System.Runtime.InteropServices.ComTypes.IDataObject)dataObject);

                    ShellFileOperations shFileOp = new(NativeMethods.InterceptClipboard.MainWindowHandle);
                    shItems.ForEach(shia => shFileOp.QueueCopyOperation(shia, tempDrag));

                    ShellItem lastTopItem = null;
                    shFileOp.PostCopyItem += (s, e) =>
                    {
                        // Skip non top level items
                        if (e.DestItem.Parent.ParsingName != Data.RuntimeSettings.TempDragPath)
                            return;

                        // A new top level item means the previous one is done
                        if (lastTopItem is not null
                            && lastTopItem.ParsingName != e.DestItem.ParsingName
                            && FileHelper.AllFilesAreApks(DragFiles))
                        {
                            ShellFileOperation.PushPackages(device, [lastTopItem], App.AppDispatcher);
                        }

                        lastTopItem = e.DestItem;
                    };

                    shFileOp.FinishOperations += (s, e) =>
                    {
                        // The last item is not caught by the PostCopyItem event
                        if (lastTopItem is not null && FileHelper.AllFilesAreApks(DragFiles))
                            ShellFileOperation.PushPackages(device, [lastTopItem], App.AppDispatcher);
                    };

                    shFileOp.PerformOperations();
                }
                // Pasted virtual files with no ShellID List Array (e.g. mail attachments), read as they're pushed.
                // The clipboard keeps the source's data available meanwhile; a drag's may be gone once dropped.
                else if (dataObject.GetDataPresent(AdbDataFormats.FileContents)
                    && !IsDrag
                    && !isAppDrive
                    && !ArchiveHelper.CanPasteIntoArchive(targetFolder, deviceId))
                {
                    VerifyAndPushContents(targetFolder, (System.Runtime.InteropServices.ComTypes.IDataObject)dataObject, Descriptors, device);
                }
                // Packages, a target archive, or a dragged source that must be read now: through the temp folder.
                // Was supposed to be the main method for zip archives, but Vanara covers that in ShellItemArray.
                else if (dataObject.GetDataPresent(AdbDataFormats.FileContents))
                {
                    Task.Run(() =>
                    {
                        string[] files = new string[Descriptors.Length];

                        for (int i = 0; i < Descriptors.Length; i++)
                        {
                            // A name stepping up a folder would be written outside the temp folder
                            if (FileHelper.HasParentReference(Descriptors[i].Name))
                            {
                                files[i] = "";
                                continue;
                            }

                            files[i] = FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, Descriptors[i].Name, '\\');
                            if (Descriptors[i].IsDirectory)
                                continue;

                            try
                            {
                                // Try to acquire the stream of each descriptor
                                var stream = VirtualFileDataObject.GetFileContents(dataObject, i);

                                // Save the stream to the temp folder, create the parent folder if it doesn't exist
                                Directory.CreateDirectory(FileHelper.GetParentPath(files[i]));

                                NativeMethods.SaveComStreamToFile(stream, files[i]);

                                // A descriptor without FD_WRITESTIME reads as 1601
                                if (Descriptors[i].ChangeTimeUtc is { Year: > 1601 } changeTimeUtc)
                                    File.SetLastWriteTime(files[i], changeTimeUtc.ToLocalTime());
                            }
                            catch (Exception e) when (e is COMException or IOException or UnauthorizedAccessException)
                            {
                                // If failed, add a failed operation to the queue
                                App.SafeInvoke(() =>
                                {
                                    Data.FileOpQ.AddOperation(
                                        new FileSyncOperation(
                                            FileOperation.OperationType.Push,
                                            Descriptors[i],
                                            new(targetFolder),
                                            device,
                                            new FailedOpProgressViewModel(e.Message)));
                                });

                                // Neither listed nor pushed below
                                files[i] = "";
                            }
                        }

                        IEnumerable<FileClass> shItems = [];
                        try
                        {
                            shItems = files
                                .Where(d => FileHelper.GetParentPath(d) == Data.RuntimeSettings.TempDragPath)
                                .Select(d => new FileClass(ShellItem.Open(d)));
                        }
                        catch
                        {
                        }
                        
                        if (shItems.Any())
                        {
                            if (isAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(device, shItems.Select(f => f.ShellItem).OfType<ShellItem>(), App.AppDispatcher);
                            }
                            else
                                VerifyAndPush(targetFolder, shItems, CurrentEffect, device);
                        }
                    });
                }
            }
            else if (IsWindows) // FileDrop format
            {
                // The source may have created its files just now (7-Zip extracts at the drop)
                string? staging = null;
                if (IsFileDropOnly)
                    staging = TakeDroppedFiles(dataObject);

                // Nothing was extracted, so there are only the stand-ins listed while dragging
                if (HasPendingFiles)
                    return;

                if (isAppDrive)
                {
                    if (FileHelper.AllFilesAreApks(DragFiles))
                        ShellFileOperation.PushPackages(device, CurrentFiles.Select(f => f.ShellItem).OfType<ShellItem>(), App.AppDispatcher);

                    if (staging is not null)
                        ReleaseDropStaging(staging);
                }
                else if (staging is not null)
                    ReleaseDropStagingAfter(VerifyAndPushAsync(targetFolder, CurrentFiles, CurrentEffect, device), staging);
                else
                    VerifyAndPush(targetFolder, CurrentFiles, CurrentEffect, device);
            }
            else if (SourceDevice?.ID == device.ID)
            {
                // Dragging a folder into itself is not allowed
                if (DragFiles.Length == 1 && DragFiles[0] == targetFolder && IsDrag)
                    return;

                if (isAppDrive)
                {
                    if (FileHelper.AllFilesAreApks(DragFiles))
                        ShellFileOperation.InstallPackages(device, CurrentFiles, App.AppDispatcher);
                }
                else
                {
                    VerifyAndPaste(isLink ? DragDropEffects.Link : CurrentEffect,
                               targetFolder,
                               CurrentFiles,
                               App.AppDispatcher,
                               device,
                               Data.CurrentPath);
                }
            }
            else
            {
                // Not supported
                return;
            }

            if (CurrentEffect is DragDropEffects.Move)
                Clear();
        }

        ReadObject();

        if (IsDrag)
            ClearDrag();
    }
}
