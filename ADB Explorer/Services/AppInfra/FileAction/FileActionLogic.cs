using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services.AppInfra;

internal static class FileActionLogic
{
    private static FileList ActionList => Data.Active;

    private static FileActionsEnable ActionFlags => ActionList.Actions;

    private static LogicalDeviceViewModel? ActionDevice => ActionList.Device ?? Data.DevicesObject?.Current;

    private static string ActionPath => ActionList.Path;

    private static NavigationTreeViewModel? ExplorerTree
        => App.Services.GetService<ExplorerViewModel>()?.Tree;

    private static bool HasRootShell => ActionDevice?.HasRootShell == true;

    private static bool SelectionIsFuseProtectedAndroidRoot =>
        Data.SelectedFiles.Any(f => ShellAccessHelper.IsFuseProtectedAndroidRoot(f.FullPath));

    private static bool IsTrashDriveSelectedInDriveView()
        => Data.FileActions.IsDriveViewVisible
           && Data.RuntimeSettings.SelectedDrive?.Type is AbstractDrive.DriveType.Trash;

    private static VirtualDriveViewModel? SelectedTrashDrive()
        => Data.RuntimeSettings.SelectedDrive is VirtualDriveViewModel { Type: AbstractDrive.DriveType.Trash } trash
            ? trash
            : null;

    private static IReadOnlyList<string> PendingCompressSourcePaths { get; set; } = [];
    private static FileClass? PendingCompressTemp { get; set; }
    public static bool IsPendingCompress { get; private set; }

    public static IReadOnlyList<string> GetPendingCompressSourcePaths() => PendingCompressSourcePaths;

    public static void BeginCompressTo(string extension)
    {
        PendingCompressSourcePaths = [.. Data.SelectedFiles.Select(f => f.FullPath)];
        PendingCompressTemp = null;
        IsPendingCompress = true;
        Data.RuntimeSettings.CompressToExtension = extension;
    }

    public static void SetPendingCompressTemp(FileClass file) => PendingCompressTemp = file;

    public static void CancelPendingCompress(FileClass? file = null)
    {
        if (!IsPendingCompress)
            return;

        if (file is not null
            && PendingCompressTemp is not null
            && !ReferenceEquals(file, PendingCompressTemp))
            return;

        IsPendingCompress = false;
        PendingCompressTemp = null;
        PendingCompressSourcePaths = [];
    }

    private static bool TryConsumePendingCompress(FileClass file, out IReadOnlyList<string> sourcePaths)
    {
        sourcePaths = [];
        if (!IsPendingCompress || !ReferenceEquals(file, PendingCompressTemp))
            return false;

        sourcePaths = PendingCompressSourcePaths;
        IsPendingCompress = false;
        PendingCompressTemp = null;
        PendingCompressSourcePaths = [];
        return true;
    }

    private static string RemoveApkMessage(IEnumerable<IBrowserItem> objects)
    {
        var count = objects.Count();

        if (count == 1)
            return string.Format(Strings.Resources.S_REM_APK, objects.First().DisplayName);

        return string.Format(Strings.Resources.S_REM_APK_PLURAL, count);
    }

    public static async void UninstallPackages()
    {
        var pkgs = Data.SelectedPackages;
        var files = Data.SelectedFiles;

        var result = await DialogService.ShowConfirmation(
            RemoveApkMessage(ActionFlags.IsAppDrive ? pkgs : files),
            Strings.Resources.S_CONF_UNI_TITLE,
            Strings.Resources.S_UNINSTALL,
            icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
            return;

        var packageTask = await Task.Run(() =>
        {
            if (ActionFlags.IsAppDrive)
                return pkgs.Select(pkg => pkg.Name);

            return files.Select(item => ShellFileOperation.GetPackageName(ActionDevice, item.FullPath));
        });

        ShellFileOperation.UninstallPackages(ActionDevice, packageTask, App.AppDispatcher);
    }

    public static void InstallPackages()
    {
        var packages = Data.SelectedFiles;

        ShellFileOperation.InstallPackages(ActionDevice, packages, App.AppDispatcher);
    }

    public static void PushPackages() => PushPackages(ActionDevice);

    public static void PushPackages(LogicalDeviceViewModel? device)
    {
        if (device is null)
            return;

        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = false,
            Multiselect = true,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = Strings.Resources.S_INSTALL_APK,
        };
        dialog.Filters.Add(new(
            Strings.Resources.S_FILE_TYPE_APK,
            string.Join(';', AdbExplorerConst.INSTALL_APK.Select(name => name[1..]).Append(AdbExplorerConst.APK_BACKUP_EXTENSION[1..].ToLowerInvariant()))));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        var shItems = dialog.FileNames.Select(ShellItem.Open);
        ShellFileOperation.PushPackages(device, shItems, App.AppDispatcher);
    }

    public static void BackupPackages()
    {
        var packages = Data.SelectedPackages.ToList();
        if (packages.Count == 0 || ActionDevice is null)
            return;

        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true,
            Multiselect = false,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = packages.Count > 1
                ? Strings.Resources.S_ITEM_DESTINATION_PLURAL
                : string.Format(Strings.Resources.S_ITEM_DESTINATION, packages[0].Name),
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        var targetPath = dialog.FileName;
        if (!Directory.Exists(targetPath) && FileHelper.GetFullName(targetPath) == packages[0].Name)
            targetPath = FileHelper.GetParentPath(targetPath);

        ShellFileOperation.BackupPackages(ActionDevice, packages, targetPath, App.AppDispatcher);
    }

    public static void UpdateModifiedDates()
    {
        ShellFileOperation.ChangeDateFromName(ActionDevice, Data.SelectedFiles, App.AppDispatcher);
    }

    public static void RestoreItems()
    {
        var listed = Data.DirList?.FileList;
        var restoreSource = !Data.SelectedFiles.Any() && listed is not null ? listed : Data.SelectedFiles;
        var restoreItems = restoreSource.Where(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        string[] existingItems = [];
        List<FileClass> existingFiles = [];
        bool merge = false;

        var restoreTask = Task.Run(() =>
        {
            existingItems = ADBService.PathsExist(ActionDevice.ID, restoreItems.Select(file => file.TrashIndex.OriginalPath));
            if (existingItems?.Length > 0)
            {
                if (restoreItems.Any(item => item.IsDirectory && existingItems.Contains(item.TrashIndex.OriginalPath)))
                    merge = true;

                existingItems = [.. existingItems.Select(path => path[(path.LastIndexOf('/') + 1)..])];
            }

            foreach (var item in restoreItems)
            {
                if (existingItems.Contains(item.FullName))
                    return;

                if (restoreItems.Count(file => file.FullName == item.FullName && file.TrashIndex.OriginalPath == item.TrashIndex.OriginalPath) > 1)
                {
                    existingItems = [.. existingItems, item.FullName];
                    existingFiles.Add(item);
                    if (item.IsDirectory)
                        merge = true;
                }
            }
        });

        restoreTask.ContinueWith((t) =>
        {
            App.SafeBeginInvoke(async () =>
            {
                if (existingItems.Length is int count and > 0)
                {
                    var result = await DialogService.ShowConfirmation(
                        count == 1
                            ? Strings.Resources.S_CONFLICT_ITEMS
                            : string.Format(Strings.Resources.S_CONFLICT_ITEMS_PLURAL, count),
                        Strings.Resources.S_RESTORE_CONF_TITLE,
                        primaryText: merge
                            ? Strings.Resources.S_MERGE_OR_REPLACE
                            : Strings.Resources.S_REPLACE,
                        secondaryText: count == restoreItems.Count() ? "" : Strings.Resources.S_SKIP,
                        cancelText: Strings.Resources.S_CANCEL,
                        icon: DialogService.DialogIcon.Exclamation);

                    if (result.Item1 is Wpf.Ui.Controls.ContentDialogResult.None)
                    {
                        return;
                    }

                    if (result.Item1 is Wpf.Ui.Controls.ContentDialogResult.Secondary)
                    {
                        restoreItems = existingFiles.Count != count
                            ? restoreItems.Where(item => !existingItems.Contains(item.FullName))
                            : restoreItems.Except(existingFiles);
                    }
                }

                ShellFileOperation.MoveItems(device: ActionDevice,
                                         items: restoreItems,
                                         targetPath: null,
                                         currentPath: ActionPath,
                                         fileList: Data.DirList?.FileList,
                                         dispatcher: App.AppDispatcher);

                var remainingItems = Data.DirList?.FileList is { } listed
                    ? listed.Except(restoreItems)
                    : [];
                TrashHelper.EnableRecycleButtons(remainingItems);

                // Clear all remaining files if none of them are indexed
                if (!remainingItems.Any(item => item.TrashIndex is not null))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(ActionDevice, remainingItems));
                }

                if (!Data.SelectedFiles.Any())
                    TrashHelper.EnableRecycleButtons();
            });
        });
    }

    public static void CopyItemPath()
    {
        var path = ActionFlags.IsAppDrive ? Data.SelectedPackages.First().Name : Data.SelectedFiles.First().FullPath;
        Clipboard.SetText(path);
    }

    public static async Task CreateNewItem(FileClass file, string newName = null, LogicalDeviceViewModel? device = null, bool selectCreated = true)
    {
        if (!string.IsNullOrEmpty(newName))
            file.UpdatePath(FileHelper.ConcatPaths(file.ParentPath, newName));

        if (Data.Settings.ShowExtensions)
            file.UpdateType();

        try
        {
            device ??= ActionDevice;
            if (device is null)
            {
                Data.DirList?.FileList.Remove(file);
                ExplorerTree?.CancelTempFile(file);
                return;
            }

            if (TryConsumePendingCompress(file, out var compressSources))
            {
                file.IsTemp = false;
                file.ModifiedTime = DateTime.Now;
                file.Size = null;
                file.IsCreationTimeResolved = false;
                file.UpdateType();

                RefreshNewItemInList(file);
                Data.ItemToSelect.Value = file;

                ShellFileOperation.CompressArchive(device, file, compressSources, App.AppDispatcher);
                return;
            }
            else if (ArchivePath.TryParse(file.FullPath, out var archivePath, out var internalPath, device.ID)
                && !string.IsNullOrEmpty(internalPath)
                && ArchiveHelper.CanPasteIntoArchive(file.FullPath, device.ID))
            {
                await Task.Run(() => ArchiveExtract.CreateTarMember(
                    device.ID,
                    archivePath,
                    internalPath,
                    file.Type is FileType.Folder,
                    Data.DeviceCts.Token));
            }
            else if (file.Type is FileType.Folder)
                await ShellFileOperation.MakeDir(device, file.FullPath);
            else if (file.Type is FileType.File)
                await ShellFileOperation.MakeFile(device, file.FullPath);
            else
                throw new NotSupportedException();
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message,
                                      Strings.Resources.S_CREATE_ERR_TITLE,
                                      DialogService.DialogIcon.Critical,
                                      copyToClipboard: true,
                                      error: DialogError.CreateFileFailed);
            Data.DirList?.FileList.Remove(file);
            ExplorerTree?.CancelTempFile(file);
            return;
        }

        file.IsTemp = false;
        file.ModifiedTime = DateTime.Now;
        if (file.Type is FileType.File)
            file.Size = 0;

        // Temp rename may have marked this resolved before the path existed on device.
        file.IsCreationTimeResolved = false;

        if (Data.DirList?.FileList is { } listing
            && device.ID == (Data.Files.Device?.ID ?? Data.DevicesObject.Current?.ID)
            && NavigationTreeNode.PathsEqual(Data.CurrentPath, file.ParentPath)
            && listing.IndexOf(file) < 0)
            listing.Insert(0, file);

        RefreshNewItemInList(file);
        ExplorerTree?.CompleteTempFile(file);
        if (selectCreated && Data.DirList?.FileList?.Contains(file) == true)
            Data.ItemToSelect.Value = file;
    }

    private static void RefreshNewItemInList(FileClass file)
    {
        if (Data.DirList?.FileList is not { } files)
            return;

        var index = files.IndexOf(file);
        if (index < 0)
            return;

        files.Remove(file);
        files.Insert(index, file);
    }

    public static void IsPasteEnabled()
    {
        // Do not update if drag is active
        if (Data.CopyPaste.IsDrag)
            return;

        var hasClipboard = Data.CopyPaste.PasteSource is not CopyPasteService.DataSource.None
            && Data.CopyPaste.Files.Length > 0;

        if (!hasClipboard)
        {
            ActionFlags.CutItemsCount.Value = "";
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

    private static void SetPasteLabels(FileActionsEnable actions)
    {
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

    public static bool EnableUiPaste()
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

    public static bool EnableKeyboardPaste()
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

        var selected = Data.SelectedFiles?.Count() > 1 ? 0 : Data.SelectedFiles?.Count();
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
                if (Data.CopyPaste.PasteState is DragDropEffects.Copy && Data.DirList?.FileList.Any(f => f.FullPath == files[0]) is true)
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
        var deviceId = Data.DevicesObject?.Current?.ID ?? "";
        var restrictions = DriveHelper.GetRestrictions(targetPath);
        return HasRootShell
            && Data.CopyPaste.Files.Length == 1
            && Data.CopyPaste.IsSelf
            && restrictions.NoSymbolicLinks is not true
            && restrictions.ReadOnly is not true
            && !ArchivePath.IsArchivePath(targetPath, deviceId);
    }

    public static DragDropEffects EnableDropPaste(FileClass target = null)
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

        var deviceId = Data.DevicesObject.Current?.ID ?? "";
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

        var deviceId = target.OwnerDevice?.ID ?? Data.DevicesObject.Current?.ID ?? "";

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

    public static void NewFolder()
    {
        if (!ReferenceEquals(Data.Active, Data.Files)
            && ExplorerTree?.ContextTarget is { } node)
        {
            ExplorerTree.QueueNewFolder(node);
            return;
        }

        Data.RuntimeSettings.NewFolder = true;
    }

    public static void ContextRename()
    {
        if (!ReferenceEquals(Data.Active, Data.Files)
            && ExplorerTree?.ContextTarget is { } node)
        {
            ExplorerTree.QueueRename(node);
            return;
        }

        Data.RuntimeSettings.Rename = true;
    }

    public static void PasteFiles(IEnumerable<FileClass> selectedFiles, bool isLink = false)
    {
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

    public static void CopyPackages(IEnumerable<Package> items)
    {
        ActionFlags.CopyEnabled = false;
        ActionFlags.CutEnabled = true;

        IsPasteEnabled();

        var vfdo = VirtualFileDataObject.PrepareTransfer(items, VirtualFileDataObject.DataObjectMethod.Clipboard);
        if (vfdo is null)
            return;

        Data.CopyPaste.UpdateSelfVFDO(isDrag: false, pasteEffect: DragDropEffects.Copy);
        vfdo.SendObjectToShell(VirtualFileDataObject.DataObjectMethod.Clipboard, allowedEffects: DragDropEffects.Copy);
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
    }

    /// <summary>
    /// Copies top-level members of the selected archive onto the clipboard (context menu only).
    /// </summary>
    public static void CopyArchiveContents()
    {
        if (!TryGetSelectedNavigableArchive(out var archive) || archive is null)
            return;

        var device = Data.DevicesObject.Current;
        var archivePath = archive.FullPath;
        var token = Data.DeviceCts.Token;

        Task.Run(() =>
        {
            List<FileClass> members;
            try
            {
                members = [.. ArchiveListing.ListEntries(device.ID, archivePath, "", token)
                    .Select(FileClass.GenerateAndroidFile)];
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Copy archive contents failed: {e.Message}");
#endif
                return;
            }

            if (members.Count == 0)
                return;

            App.SafeInvoke(() =>
            {
                CutFiles(members, isCopy: true);
                UpdateFileActions();
            });
        }, token);
    }

    /// <summary>
    /// Extracts top-level members of a selected archive, or of a clipboard archive file, into the current folder.
    /// </summary>
    public static void ExtractArchiveHere()
    {
        if (ActionDevice is not { } device)
            return;

        string? archivePath = null;
        var targetFolder = ActionPath;

        if (TryGetSelectedNavigableArchive(out var selectedArchive))
        {
            archivePath = selectedArchive.FullPath;
            targetFolder = ActionPath;
        }
        else if (IsClipboardSingleArchiveFileCopy())
        {
            archivePath = Data.CopyPaste.Files[0];
            targetFolder = GetUiPasteTargetPath();
        }

        if (archivePath is null
            || ArchivePath.IsArchivePath(targetFolder, device.ID)
            || !DriveHelper.IsModificationAllowedAt(targetFolder, device.ID))
            return;

        var token = Data.DeviceCts.Token;

        Task.Run(() =>
        {
            List<FileClass> members;
            try
            {
                members = [.. ArchiveListing.ListEntries(device.ID, archivePath, "", token)
                    .Select(FileClass.GenerateAndroidFile)];
            }
            catch (Exception e)
            {
#if !DEPLOY
                DebugLog.PrintLine($"Extract archive here failed: {e.Message}");
#endif
                return;
            }

            if (members.Count == 0)
                return;

            App.SafeInvoke(() => Data.CopyPaste.VerifyAndPaste(
                DragDropEffects.Copy,
                targetFolder,
                members,
                App.AppDispatcher,
                device,
                Data.CurrentPath));
        }, token);
    }

    public static void Rename(TextBox textBox)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        var vm = file.ActiveViewModel;
        var name = FileHelper.DisplayName(textBox);

        if (!vm.IsRenameUnixLegal
            || (DriveHelper.GetRestrictions(file.FullPath).RestrictedNaming && !vm.IsRenameNamingLegal)
            || !vm.IsRenameUnique)
        {
            return;
        }

        if (file.IsTemp)
        {
            if (string.IsNullOrEmpty(textBox.Text))
            {
                CancelPendingCompress(file);
                Data.DirList.FileList.Remove(file);
                return;
            }

            var newName = textBox.Text;
            if (!Data.Settings.ShowExtensions)
                newName += file.Extension;

            _ = CreateNewItem(file, newName);
        }
        else if (!string.IsNullOrEmpty(textBox.Text) && textBox.Text != name)
        {
            try
            {
                string text = textBox.Text;
                if (text.Count(c => c == TextHelper.RTL_MARK) == 1)
                    text = text.Replace($"{TextHelper.RTL_MARK}", "");

                FileHelper.RenameFile(file, text);
            }
            catch (Exception)
            { }
        }
    }

    public static void RenameTreeNode(NavigationTreeNode node, TextBox textBox)
    {
        if (node.File is not { } file)
            return;

        if (!node.IsRenameUnixLegal
            || (DriveHelper.GetRestrictions(node.Path, node.OwnerDevice).RestrictedNaming && !node.IsRenameNamingLegal)
            || !node.IsRenameUnique)
        {
            if (file.IsTemp)
                ExplorerTree?.CancelTempFile(file);
            return;
        }

        if (file.IsTemp)
        {
            if (string.IsNullOrEmpty(textBox.Text))
            {
                ExplorerTree?.CancelTempFile(file);
                return;
            }

            _ = CreateNewItem(file, textBox.Text, node.OwnerDevice, selectCreated: false);
            return;
        }

        var name = node.DisplayName;
        if (string.IsNullOrEmpty(textBox.Text) || textBox.Text == name)
            return;

        try
        {
            var text = textBox.Text;
            if (text.Count(c => c == TextHelper.RTL_MARK) == 1)
                text = text.Replace($"{TextHelper.RTL_MARK}", "");

            FileHelper.RenameFile(file, text, node.OwnerDevice);
            var newPath = FileHelper.ConcatPaths(FileHelper.GetParentPath(node.Path), text);
            node.DisplayName = text;
            node.UpdatePath(newPath);
            file.UpdatePath(newPath);
        }
        catch (Exception)
        { }
    }

    public static async void DeleteFiles(bool? permanent = null)
    {
        permanent ??= Keyboard.Modifiers is ModifierKeys.Shift;

        // Snapshot everything derived from Data.Active up front: a tree context menu's Data.Use scope
        // ends asynchronously once the menu closes (see NavigationPane.TreeContextMenu_Closed), which
        // can happen before the confirmation dialog below is answered, reverting Data.Active in the
        // meantime and making the live ActionDevice / ActionPath / Data.DirList properties unsafe to
        // read again after the first await.
        var device = ActionDevice;
        if (device is null)
            return;

        var isRecycleBin = ActionFlags.IsRecycleBin;
        var hasRootShell = device.HasRootShell;
        var actionPath = ActionPath;
        var dirFileList = Data.DirList?.FileList;

        var emptyTrashFromDriveView = IsTrashDriveSelectedInDriveView() && !Data.SelectedFiles.Any();
        var emptyingRecycleBin = (isRecycleBin && !Data.SelectedFiles.Any()) || emptyTrashFromDriveView;

        List<FileClass> itemsToDelete;
        if (emptyingRecycleBin)
        {
            if (emptyTrashFromDriveView || dirFileList is null)
                itemsToDelete = TrashHelper.GetRecycleBinItems();
            else
                itemsToDelete = [.. dirFileList.Where(f => f.Extension != AdbExplorerConst.RECYCLE_INDEX_SUFFIX)];
        }
        else
        {
            itemsToDelete = [.. hasRootShell
                ? Data.SelectedFiles
                : Data.SelectedFiles.Where(file => file.Type is FileType.File or FileType.Folder)];
        }
        
        string deletedString;
        if (itemsToDelete.Count == 1)
            deletedString = FileHelper.DisplayName(itemsToDelete.First());
        else
        {
            deletedString = $"{itemsToDelete.Count} ";
            if (itemsToDelete.All(item => item.IsDirectory))
                deletedString += Strings.Resources.S_MENU_FOLDERS;
            else if (itemsToDelete.All(item => !item.IsDirectory))
                deletedString += Strings.Resources.S_MENU_FILES;
            else
                deletedString += Strings.Resources.S_BROWSER_ITEMS_PLURAL;
        }

        if (!isRecycleBin && !emptyTrashFromDriveView && Data.Settings.EnableRecycle && !permanent.Value)
        {
            // Archive members cannot be moved to the recycle bin — always permanent-delete them.
            if (itemsToDelete.Any(f => ArchivePath.IsArchivePath(f.FullPath, device.ID)))
            {
                permanent = true;
            }
        }

        if (!Data.Settings.EnableRecycle || permanent.Value || emptyingRecycleBin)
        {
            var result = await DialogService.ShowConfirmation(
            string.Format(Strings.Resources.S_DELETE_PERMANENT, deletedString),
            Strings.Resources.S_DEL_CONF_TITLE,
            emptyingRecycleBin ? Strings.Resources.S_EMPTY_TRASH : Strings.Resources.S_DELETE_ACTION,
            icon: DialogService.DialogIcon.Delete);

            if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
                return;
        }

        if (!isRecycleBin && !emptyTrashFromDriveView && Data.Settings.EnableRecycle && !permanent.Value)
        {
            await ShellFileOperation.MakeDir(device, AdbExplorerConst.RECYCLE_PATH);

            ShellFileOperation.MoveItems(device,
                                         itemsToDelete,
                                         AdbExplorerConst.RECYCLE_PATH,
                                         actionPath,
                                         dirFileList,
                                         App.AppDispatcher);
        }
        else
        {
            ShellFileOperation.DeleteItems(device, itemsToDelete, App.AppDispatcher);

            if (emptyingRecycleBin)
                TrashHelper.GetTrashDrive(device)?.SetItemsCount(0);

            if (isRecycleBin)
            {
                var remainingItems = dirFileList is { } listed
                    ? listed.Except(itemsToDelete)
                    : [];
                TrashHelper.EnableRecycleButtons(remainingItems);

                // Clear all remaining files if none of them are indexed
                if (!remainingItems.Any(item => item.TrashIndex is not null))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(device, remainingItems));
                }
            }
            else if (emptyTrashFromDriveView)
            {
                var indexPaths = ADBService.FindFilesInPath(device.ID,
                                                            AdbExplorerConst.RECYCLE_PATH,
                                                            includeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);
                if (indexPaths.Length > 0)
                    ShellFileOperation.SilentDelete(device, indexPaths);
            }
        }
    }

    public static void Refresh()
    {
        if (Data.FileActions.IsAppDrive)
        {
            UpdatePackages(updateExplorer: true, cacheOnly: true);
            return;
        }

        if (Data.FileActions.IsDriveViewVisible)
        {
            Data.RuntimeSettings.LocationToNavigate = new(Navigation.SpecialLocation.DriveView);
            return;
        }

        Data.RuntimeSettings.LocationToNavigate = new(Data.CurrentPath);
    }

    public static void NavRefresh()
    {
        if (Data.FileActions.IsSearchMode && Data.FileActions.ListingInProgress)
        {
            StopSearch();
            return;
        }

        Refresh();
    }

    public static void StopSearch()
    {
        if (!Data.FileActions.IsSearchMode || !Data.FileActions.ListingInProgress)
            return;

        Data.DeviceCts.Cancel();
        Data.DirList?.Stop();
        ApkIconService.CancelPending();
    }

    public static void RefreshDrives(bool asyncClassify, CancellationToken cancellationToken)
    {
        if (Data.DevicesObject.Current is null)
            return;

        if (!asyncClassify && Data.DevicesObject.Current.Drives?.Count > 0 && !Data.FileActions.IsExplorerVisible)
            asyncClassify = true;

        var driveTask = Task.Run(() =>
        {
            if (Data.DevicesObject.Current is null)
                return null;

            bool countRecycle = false, countPackages = false, countInstallers = false;
            if (Data.DevicesObject.Current.Type is not DeviceType.Recovery)
            {
                countRecycle = Data.Settings.EnableRecycle && Data.DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Trash);
                countInstallers = Data.Settings.EnableApk && Data.DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Temp);
                countPackages = Data.Settings.EnableApk && Data.DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Package);
            }

            return ADBService.GetDrives(
                Data.DevicesObject.Current.ID,
                Data.DevicesObject.Current.Type,
                cancellationToken,
                countRecycle,
                countPackages,
                countInstallers,
                Data.Settings.ShowSystemPackages);
        }, cancellationToken);

        driveTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || t.Result is null)
                return;

            var result = t.Result.Value;
            App.SafeInvoke(async () =>
            {
                if (Data.DevicesObject.Current?.Type is DeviceType.Recovery)
                {
                    foreach (var item in Data.DevicesObject.Current.Drives.OfType<VirtualDriveViewModel>())
                        item.SetItemsCount(item.Type is AbstractDrive.DriveType.Package ? -1 : null);
                }
                else
                {
                    ApplyVirtualDriveCounts(result);
                }

                var device = Data.DevicesObject.Current;
                if (device is null || App.AppDispatcher is null)
                    return;

                if (await device.UpdateDrives(result.Drives, App.AppDispatcher, asyncClassify))
                {
                    Data.RuntimeSettings.FilterDrives = true;
                    FolderHelper.CombineDisplayNames();
                }
            });
        }, cancellationToken);
    }

    private static void ApplyVirtualDriveCounts(DrivePollResult result)
    {
        if (Data.DevicesObject.Current is null)
            return;

        if (result.RecycleCount is long recycleCount)
        {
            var trash = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Trash);
            ((VirtualDriveViewModel)trash)?.SetItemsCount(recycleCount);
        }

        if (result.InstallersCount is ulong installersCount)
        {
            var temp = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Temp);
            ((VirtualDriveViewModel)temp)?.SetItemsCount((long)installersCount);
        }

        if (result.PackagesCount is ulong packagesCount)
        {
            var package = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
            ((VirtualDriveViewModel)package)?.SetItemsCount((int)packagesCount);
        }
    }

    public static void UpdateInstallersCount(CancellationToken cancellationToken = default)
    {
        var countTask = Task.Run(() => ADBService.CountPackages(Data.DevicesObject.Current.ID), cancellationToken);
        countTask.ContinueWith((t) => App.SafeInvoke(() =>
        {
            if (!t.IsCanceled && Data.DevicesObject.Current is not null)
            {
                var temp = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Temp);
                ((VirtualDriveViewModel)temp)?.SetItemsCount((long)t.Result);
            }
        }), cancellationToken);
    }

    public static void UpdatePackagesCount(CancellationToken cancellationToken = default)
    {
        var packageTask = Task.Run(() => ShellFileOperation.GetPackagesCount(Data.DevicesObject.Current, Data.Settings.ShowSystemPackages), cancellationToken);

        packageTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || t.Result is null || Data.DevicesObject.Current is null)
                return;

            App.SafeInvoke(() =>
            {
                var package = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
                ((VirtualDriveViewModel)package)?.SetItemsCount((int?)t.Result);
            });
        });
    }

    public static void UpdatePackages(bool updateExplorer = false, CancellationToken cancellationToken = default, bool cacheOnly = false)
    {
        Data.FileActions.ListingInProgress = true;

        var version = Data.DevicesObject.Current.AndroidVersion;
        var packageTask = Task.Run(() => ShellFileOperation.GetPackages(Data.DevicesObject.Current, Data.Settings.ShowSystemPackages, version is not null && version >= AdbExplorerConst.MIN_PKG_UID_ANDROID_VER), cancellationToken);

        packageTask.ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            App.SafeInvoke(() =>
            {
                var listed = t.Result;

                if (cacheOnly)
                {
                    MergePackageList(listed);
                    ApkIconService.ApplyCacheToPackages(Data.Packages);
                }
                else
                {
                    Data.Packages = listed;
                }

                if (updateExplorer)
                {
                    var explorer = App.Services.GetService<ExplorerViewModel>();
                    if (!ReferenceEquals(explorer.ExplorerSource, Data.Packages))
                        explorer.ExplorerSource = Data.Packages;
                }

                if (!updateExplorer && Data.DevicesObject.Current is not null)
                {
                    var package = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
                    ((VirtualDriveViewModel)package)?.SetItemsCount(Data.Packages.Count);
                }

                Data.FileActions.ListingInProgress = false;
                UpdateFileActions();
                CommandManager.InvalidateRequerySuggested();

                if (!cacheOnly
                    && updateExplorer
                    && Data.FileActions.IsAppDrive
                    && ApkIconService.IsEnabled)
                {
                    ApkIconService.BeginPreloadPackages(Data.Packages);
                }
            });
        });
    }

    /// <summary>
    /// Updates <see cref="Data.Packages"/> to match a fresh <c>pm list</c> without replacing
    /// existing instances (keeps in-memory icons/labels).
    /// </summary>
    private static void MergePackageList(ObservableList<Package> listed)
    {
        var incomingByName = listed.ToDictionary(pkg => pkg.Name, StringComparer.Ordinal);
        Data.Packages.RemoveAll(pkg => !incomingByName.ContainsKey(pkg.Name));

        var existingByName = Data.Packages.ToDictionary(pkg => pkg.Name, StringComparer.Ordinal);

        foreach (var pkg in listed)
        {
            if (existingByName.TryGetValue(pkg.Name, out var existing))
            {
                existing.Path = pkg.Path;
                existing.Type = pkg.Type;
                existing.Uid = pkg.Uid;
                existing.Version = pkg.Version;
                existing.DeviceSerial = pkg.DeviceSerial;
                continue;
            }

            Data.Packages.Add(pkg);
        }
    }

    public static void ClearExplorer(bool clearDevice = true)
    {
        App.SafeInvoke(() =>
        {
            Data.DirList?.FileList?.Clear();
            Data.Packages.Clear();
            Data.SelectedFiles = [];
            Data.SelectedPackages = [];

            Data.FileActions.PushFilesFoldersEnabled =
            Data.FileActions.PullEnabled =
            Data.FileActions.DeleteEnabled =
            Data.FileActions.BackupPackageEnabled =
            Data.FileActions.RenameEnabled =
            Data.FileActions.HomeEnabled =
            Data.FileActions.NewEnabled =
            Data.FileActions.PasteEnabled =
            Data.FileActions.IsUninstallVisible.Value =
            Data.FileActions.CutEnabled =
            Data.FileActions.CopyEnabled =
            Data.FileActions.IsExplorerVisible =
            Data.FileActions.IsSearchMode =
            Data.FileActions.PackageActionsEnabled =
            Data.FileActions.IsCopyItemPathEnabled =
            Data.FileActions.UpdateModifiedEnabled =
            Data.FileActions.IsFollowLinkEnabled =
            Data.RuntimeSettings.IsExplorerLoaded =
            Data.FileActions.ParentEnabled = false;

            Data.FileActions.IsCutPasteDeleteVisible.Value = true;
            Data.FileActions.IsPullCopyVisible.Value = true;
            Data.FileActions.IsPasteVisible.Value = true;

            Data.FileActions.IsAppDrive = false;
            Data.FileActions.IsRecycleBin = false;
            Data.FileActions.IsArchive = false;
            Data.FileActions.IsTemp = false;

            Data.FileActions.ExplorerFilter = "";

            if (clearDevice)
            {
                Data.CurrentDisplayNames.Clear();
                Data.CurrentPath = null;
                Data.DirList?.ClearCurrentLocation();
                Data.RaiseClearNavigationBox();

                UpdateFileActions();
            }
        });

        Data.RuntimeSettings.FilterActions = true;
    }

    public static void UpdateFileActions() => UpdateFileActions(Data.Files);

    public static void UpdateFileActions(FileList list)
    {
        using (Data.Use(list))
            UpdateFileActionsCore(list);
    }

    private static void UpdateFileActionsCore(FileList list)
    {
        var actions = list.Actions;
        var selectedFiles = list.SelectedFiles ?? [];
        var selectedPackages = list.SelectedPackages ?? [];
        var hasFileSelection = selectedFiles.Any();
        var hasPackageSelection = selectedPackages.Any();
        var singleFileSelected = selectedFiles.Count() == 1;
        var singlePackageSelected = selectedPackages.Count() == 1;
        var selectedFile = singleFileSelected ? selectedFiles.First() : null;

        var isAppDrive = actions.IsAppDrive;
        var isRecycleBin = actions.IsRecycleBin;
        var isSearchMode = actions.IsSearchMode;
        var isExplorerVisible = actions.IsExplorerVisible;
        var isDriveViewVisible = actions.IsDriveViewVisible;
        var enableApk = Data.Settings.EnableApk;
        var currentDevice = list.Device ?? Data.DevicesObject?.Current;
        var isNotRecovery = currentDevice?.Type is not DeviceType.Recovery;
        var hasRoot = HasRootShell;
        var fuseProtectedRoot = SelectionIsFuseProtectedAndroidRoot;

        actions.IsApkActionsVisible.Value = enableApk && currentDevice is not null;
        actions.PushPackageEnabled = actions.IsApkActionsVisible && isNotRecovery;

        actions.UninstallPackageEnabled = isAppDrive && hasPackageSelection;
        actions.ContextPushPackagesEnabled = isAppDrive && !hasPackageSelection;

        actions.IsRefreshEnabled = isDriveViewVisible || isExplorerVisible;
        actions.IsPushMenuVisible.Value = !isSearchMode;
        UpdateNavRefreshActionState();
        actions.IsCopyCurrentPathEnabled = isExplorerVisible
            && !isRecycleBin
            && !isAppDrive
            && !isSearchMode;

        actions.IsOpenApkLocationEnabled = isAppDrive && singlePackageSelected;
        actions.IsApkWebSearchEnabled = actions.IsOpenApkLocationEnabled
            && !string.IsNullOrEmpty(Data.RuntimeSettings.DefaultBrowserPath);

        actions.IsRegularItem = !hasFileSelection || hasRoot
            || selectedFiles.AnyAll(item => item.Type is FileType.File or FileType.Folder);

        var isRegularItem = actions.IsRegularItem;

        actions.IsSingleFolder = singleFileSelected
            && selectedFile is not null
            && CanEnterSelection(selectedFile);

        actions.IsFollowLinkEnabled = !isRecycleBin
            && singleFileSelected
            && selectedFile is { IsLink: true, Type: not FileType.BrokenLink };

        var isFollowLinkEnabled = actions.IsFollowLinkEnabled;
        var followLinkAllowsAction = !isFollowLinkEnabled || hasRoot;

        var listingPath = list.DirList?.CurrentPath ?? list.Path;
        var restrictions = DriveHelper.GetRestrictions(listingPath, currentDevice);
        var deviceId = currentDevice?.ID;
        if (deviceId is not null && !string.IsNullOrEmpty(listingPath))
            actions.IsArchive = ArchivePath.IsArchivePath(listingPath, deviceId);

        var isArchive = actions.IsArchive;

        bool isWritable;
        if (restrictions.ReadOnly is true)
            isWritable = false;
        else if (isSearchMode)
            isWritable = Data.SearchOriginCanWrite;
        else if (list.CanWrite is bool canWrite)
            isWritable = canWrite;
        else
            isWritable = list.DirList?.CurrentLocation?.CanWriteLocation == true;

        var canPasteIntoTar = deviceId is not null
            && ArchiveHelper.CanPasteIntoArchive(list.Path ?? "", deviceId);

        var archiveAllowsModify = !isArchive || canPasteIntoTar;
        var isExplorerFolder = isExplorerVisible
            && !isRecycleBin
            && !isAppDrive
            && !isArchive
            && !isSearchMode;

        actions.IsCurrentLocationReadOnly = (isExplorerFolder || canPasteIntoTar) && !isWritable;
        actions.IsSelectionFuseProtectedAndroidRoot = hasFileSelection && fuseProtectedRoot;

        // Push into modifiable tar is allowed; New File/Folder inside modifiable tar is allowed.
        var isSearchFolderTarget = isSearchMode
            && singleFileSelected
            && selectedFile is { IsDirectory: true };

        actions.PushFilesFoldersEnabled = isWritable && (isExplorerFolder || canPasteIntoTar || isSearchFolderTarget);
        actions.NewEnabled = isWritable && (isExplorerFolder || canPasteIntoTar);
        actions.IsNewMenuVisible.Value = isExplorerVisible
            && !isRecycleBin
            && !isAppDrive
            && actions.NewEnabled;

        if (isRecycleBin)
        {
            if (hasFileSelection)
                TrashHelper.EnableRecycleButtons(selectedFiles);
            else if (list.DirList?.FileList is { } recycleFiles)
                TrashHelper.EnableRecycleButtons(recycleFiles);
            else
            {
                var count = TrashHelper.EnsureRecycleCount(currentDevice, list.CurrentDrive as VirtualDriveViewModel);
                actions.DeleteEnabled = count > 0;
                actions.RestoreEnabled = false;
            }
        }
        else if (SelectedTrashDrive() is { ItemsCount: > 0 })
        {
            actions.DeleteEnabled = true;
            actions.RestoreEnabled = false;
        }
        else
        {
            actions.DeleteEnabled = isWritable
                && !fuseProtectedRoot
                && hasFileSelection
                && isRegularItem
                && followLinkAllowsAction
                && archiveAllowsModify;

            actions.RestoreEnabled = false;
        }

        actions.PullDescription.Value = isFollowLinkEnabled
            ? Strings.Resources.S_PULL_ACTION_LINK
            : Strings.Resources.S_PULL_ACTION;

        if (isRecycleBin)
        {
            var recycleDelete = hasFileSelection
                ? Strings.Resources.S_PERM_DEL
                : Strings.Resources.S_EMPTY_TRASH;
            actions.DeleteDescription.Value = recycleDelete;
            actions.ContextDeleteDescription.Value = recycleDelete;
        }
        else if (IsTrashDriveSelectedInDriveView())
        {
            actions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
            actions.ContextDeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
        }
        else
        {
            actions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
            actions.ContextDeleteDescription.Value =
                isArchive || Keyboard.Modifiers is ModifierKeys.Shift
                    ? Strings.Resources.S_PERM_DEL
                    : Strings.Resources.S_DELETE_ACTION;
        }

        actions.RestoreDescription.Value = isRecycleBin && !hasFileSelection
            ? Strings.Resources.S_RESTORE_ALL
            : Strings.Resources.S_RESTORE_ACTION;

        var noBrokenLinks = selectedFiles.AnyAll(f => f.Type is not FileType.BrokenLink);

        actions.IsSelectionIllegalOnWindows = hasFileSelection
            && !FileHelper.FileNameLegal(selectedFiles, FileHelper.RenameTarget.Windows);

        actions.IsSelectionIllegalNaming = !isRecycleBin
            && !isAppDrive
            && !isArchive
            && hasFileSelection
            && !FileHelper.FileNameLegal(selectedFiles, FileHelper.RenameTarget.RestrictedNaming);

        actions.IsSelectionIllegalOnWinRoot = hasFileSelection
            && !FileHelper.FileNameLegal(selectedFiles, FileHelper.RenameTarget.WinRoot);

        actions.IsSelectionConflictingNames = restrictions.CaseInsensitiveNames
            && selectedFiles.Select(f => f.FullName).Distinct(StringComparer.InvariantCultureIgnoreCase).Count() != selectedFiles.Count();

        // Pull from archive extracts selected members to /data/local/tmp then pulls (same as PrepareDescriptors).
        if (isAppDrive)
        {
            actions.PullEnabled = hasPackageSelection;
        }
        else
        {
            var canPullArchiveMembers = !isArchive
                || selectedFiles.AnyAll(f =>
                    ArchivePath.TryParse(f.FullPath, out _, out var inner, deviceId)
                    && !string.IsNullOrEmpty(inner));

            actions.PullEnabled = !isRecycleBin
                && noBrokenLinks
                && isRegularItem
                && !actions.IsSelectionIllegalOnWindows
                && !actions.IsSelectionIllegalNaming
                && !actions.IsSelectionConflictingNames
                && canPullArchiveMembers;
        }

        var pasteDeviceId = deviceId ?? "";
        var singlePasteTarget = singleFileSelected
            && selectedFile is not null
            && ArchiveHelper.IsPasteTargetContainer(selectedFile, pasteDeviceId);

        actions.ContextPushEnabled = isWritable
            && !isRecycleBin
            && !isAppDrive
            && archiveAllowsModify
            && (isSearchMode
                ? singlePasteTarget
                : !hasFileSelection || singlePasteTarget);

        actions.RenameEnabled = isWritable
            && archiveAllowsModify
            && !fuseProtectedRoot
            && !isRecycleBin
            && singleFileSelected
            && isRegularItem
            && followLinkAllowsAction;

        var allSelectedAreCut = Data.CopyPaste.IsFromDevice(list.Device ?? ActionDevice)
            && Data.CopyPaste.Files.Length == selectedFiles.Count()
            && Data.CopyPaste.Files.AnyAll(item => selectedFiles.Any(f => NavigationTreeNode.PathsEqual(f.FullPath, item)));

        var cutIsMove = allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Move;
        var cutIsCopy = allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Copy;
        var cutIsLink = allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Link;

        // Cut from archive is not supported (extract is copy-only).
        actions.CutEnabled = isWritable
            && !isArchive
            && !fuseProtectedRoot
            && noBrokenLinks
            && !cutIsMove
            && isRegularItem
            && followLinkAllowsAction;

        if (list.ForbidDestructive)
        {
            actions.CutEnabled = false;
            actions.RenameEnabled = false;
            if (!isRecycleBin)
                actions.DeleteEnabled = false;
        }

        if (isAppDrive)
        {
            actions.CopyEnabled = hasPackageSelection;
        }
        else
        {
            actions.CopyEnabled = noBrokenLinks
                && !cutIsCopy
                && !cutIsLink
                && isRegularItem
                && !isRecycleBin;
        }

        IsPasteEnabled();

        // APK enabled in settings
        // All selected files are installable
        // Not in trash or recovery
        var allInstallApk = selectedFiles.AnyAll(file => file.IsInstallApk);
        var allInstallOrBackup = selectedFiles.AnyAll(file =>
            file.IsInstallApk || AppBackupHelper.IsApkBackup(file.FullName));
        actions.PackageActionsEnabled = enableApk
            && allInstallOrBackup
            && !isRecycleBin
            && isNotRecovery;

        if (isAppDrive)
            actions.IsCopyItemPathEnabled = singlePackageSelected;
        else
            actions.IsCopyItemPathEnabled = singleFileSelected && !isRecycleBin;

        actions.CopyPathDescription.Value = isAppDrive
            ? Strings.Resources.S_COPY_APK_NAME
            : Strings.Resources.S_COPY_PATH;

        var isTreeList = !ReferenceEquals(list, Data.Files);
        var contextNewOnFolder = isTreeList
            && singleFileSelected
            && selectedFile is { IsDirectory: true };

        actions.ContextNewEnabled = isWritable
            && !isRecycleBin
            && !isAppDrive
            && archiveAllowsModify
            && (!hasFileSelection || contextNewOnFolder);

        actions.SubmenuUninstallEnabled = allInstallApk && isNotRecovery;

        actions.UpdateModifiedEnabled = isWritable
            && !isRecycleBin
            && selectedFiles.AnyAll(file => file.Type is FileType.File && !file.IsApk && !file.IsLink);

        string? pasteLinkTarget;
        if (!hasFileSelection)
            pasteLinkTarget = list.Path;
        else if (singleFileSelected && selectedFile is { IsDirectory: true })
            pasteLinkTarget = selectedFile.IsLink ? selectedFile.LinkTarget : selectedFile.FullPath;
        else
            pasteLinkTarget = null;

        actions.IsPasteLinkEnabled = !isAppDrive
            && !isRecycleBin
            && !list.ForbidPaste
            && pasteLinkTarget is not null
            && Data.CopyPaste.Files.Length == 1
            && Data.CopyPaste.IsSelf
            && Data.CopyPaste.PasteState is DragDropEffects.Copy or DragDropEffects.Link
            && IsSymlinkPasteAllowed(pasteLinkTarget);

        actions.IsCopyLinkEnabled = DriveHelper.GetRestrictions(listingPath, currentDevice).NoSymbolicLinks is not true
            && hasRoot
            && singleFileSelected
            && noBrokenLinks
            && isRegularItem
            && !isRecycleBin
            && !cutIsLink;

        var clipboardIsSelectedArchiveContents = IsClipboardContentsOfSelectedArchive();
        actions.IsCopyContentsEnabled =
            TryGetSelectedNavigableArchive(out _)
            && !clipboardIsSelectedArchiveContents;

        actions.IsExtractHereEnabled =
            (CanExtractSelectedArchiveHere() && !clipboardIsSelectedArchiveContents)
            || CanExtractClipboardArchiveHere();

        var tarAvailable = currentDevice is not null
            && ShellCommands.TarExists(currentDevice.ID);
        actions.IsCompressToEnabled.Value = actions.NewEnabled
            && !isArchive
            && tarAvailable;
        actions.IsCompressToContextEnabled = actions.IsCompressToEnabled
            && hasFileSelection;

        actions.BackupPackageEnabled = isAppDrive
            && hasPackageSelection
            && tarAvailable
            && isNotRecovery;

        actions.InstallPackageEnabled = isNotRecovery;

        if (list.DirList is null)
        {
            if (ReferenceEquals(list, Data.Files))
            {
                actions.NewEnabled = false;
                actions.ContextNewEnabled = false;
                actions.RenameEnabled = false;
            }

            actions.IsCompressToEnabled.Value = false;
            actions.IsCompressToContextEnabled = false;
            actions.IsSingleFolder = false;
        }

        Data.FileActions.IsContextNewFileVisible.Value = !isTreeList;
        Data.FileActions.IsContextNewArchiveVisible.Value = !isTreeList && Data.FileActions.IsCompressToEnabled;

        if (!ReferenceEquals(list, Data.Files))
            Data.FileActions.ContextDeleteDescription.Value = actions.ContextDeleteDescription.Value;

        if (!Data.CopyPaste.IsDrag && ReferenceEquals(list, Data.Files))
            Data.RuntimeSettings.FilterActions = true;
    }

    /// <summary>
    /// True when the self clipboard holds member paths from <see cref="CopyArchiveContents"/>
    /// for the currently selected archive.
    /// </summary>
    private static bool IsClipboardContentsOfSelectedArchive()
    {
        if (!TryGetSelectedNavigableArchive(out var archive)
            || archive is null
            || !Data.CopyPaste.IsSelf
            || Data.CopyPaste.PasteState is not DragDropEffects.Copy
            || Data.CopyPaste.Files.Length == 0
            || ActionDevice is not { } device)
            return false;

        var selectedArchive = archive.FullPath;

        foreach (var path in Data.CopyPaste.Files)
        {
            if (!ArchivePath.TryParse(path, out var archivePath, out var internalPath, device.ID)
                || string.IsNullOrEmpty(internalPath)
                || !string.Equals(archivePath, selectedArchive, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private static bool TryGetSelectedNavigableArchive(out FileClass? archive)
    {
        archive = null;

        if (ActionDevice is not { } device
            || ActionFlags.IsAppDrive
            || ActionFlags.IsRecycleBin
            || !ActionFlags.IsRegularItem
            || Data.SelectedFiles.Count() != 1
            || Data.SelectedFiles.First() is not { } selected)
            return false;

        if (!ArchiveHelper.CanNavigateIntoArchive(
                selected.FullPath,
                selected.FullName,
                device.ID,
                ActionFlags.IsArchive))
            return false;

        archive = selected;
        return true;
    }

    private static bool CanExtractSelectedArchiveHere()
    {
        if (ActionDevice is not { } device
            || !TryGetSelectedNavigableArchive(out _))
            return false;

        var target = ActionPath;
        return !ArchivePath.IsArchivePath(target, device.ID)
            && DriveHelper.IsModificationAllowedAt(target, device.ID);
    }

    private static bool CanExtractClipboardArchiveHere()
    {
        if (!IsClipboardSingleArchiveFileCopy()
            || ActionDevice is not { } device)
            return false;

        var target = GetUiPasteTargetPath();
        return !ArchivePath.IsArchivePath(target, device.ID)
            && DriveHelper.IsModificationAllowedAt(target, device.ID);
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

    /// <summary>
    /// True when the self clipboard holds one archive <em>file</em> (ordinary Copy), not member paths.
    /// </summary>
    private static bool IsClipboardSingleArchiveFileCopy()
    {
        if (!Data.CopyPaste.IsSelf
            || Data.CopyPaste.PasteState is not DragDropEffects.Copy
            || Data.CopyPaste.Files.Length != 1
            || ActionDevice is not { } device)
            return false;

        var path = Data.CopyPaste.Files[0];
        if (ArchivePath.IsArchivePath(path, device.ID))
            return false;

        return ArchiveHelper.IsNavigableArchive(FileHelper.GetFullName(path), device.ID);
    }

    private static void UpdateNavRefreshActionState()
    {
        if (Data.FileActions.IsSearchMode && Data.FileActions.ListingInProgress)
        {
            Data.FileActions.NavRefreshDescription.Value = string.Format(
                Strings.Resources.S_MENU_STOP_LOADING,
                SearchStopPathLabel());
            Data.FileActions.NavRefreshIcon.Value = new BaseIcon("\uE711", 16);
        }
        else
        {
            Data.FileActions.NavRefreshDescription.Value = Strings.Resources.S_MENU_REFRESH;
            Data.FileActions.NavRefreshIcon.Value = new BaseIcon("\uE72C", 16);
        }
    }

    private static string SearchStopPathLabel()
    {
        var root = Data.SearchOriginPath;
        if (string.IsNullOrEmpty(root))
            return Data.FileActions.ExplorerFilter ?? "";

        return Data.CurrentDisplayNames.TryGetValue(root, out var displayName)
            ? displayName
            : FileHelper.GetFullName(root);
    }

    public static void PushItems(bool isFolderPicker, bool isContextMenu)
    {
        Data.RaiseFocusNavigationBox(false);

        string targetPath, targetName = "";
        string title = "";
        var deviceId = ActionDevice?.ID ?? "";
        if (isContextMenu && Data.SelectedFiles.Count() == 1)
        {
            var selected = Data.SelectedFiles.First();
            var path = selected.IsLink ? selected.LinkTarget : selected.FullPath;
            targetPath = ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
            targetName = selected.FullName;

            title = isFolderPicker
                ? Strings.Resources.S_SELECT_FOLDER_PUSH_DESTINATION
                : Strings.Resources.S_SELECT_FILE_PUSH_DESTINATION;
        }
        else
        {
            targetPath = GetUiPasteTargetPath();

            title = isFolderPicker
                ? Strings.Resources.S_SELECT_FOLDER_PUSH
                : Strings.Resources.S_SELECT_FILE_PUSH;
        }
        
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = isFolderPicker,
            Multiselect = true,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = title,
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        var shItems = dialog.FileNames.Select(ShellItem.Open);
        
        CopyPasteService.VerifyAndPush(targetPath, shItems);
    }

    public static FileSyncOperation PushShellObject(
        ShellItem item,
        string targetPath,
        DragDropEffects dropEffects = DragDropEffects.Copy,
        ShellItem originalShellItem = null,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null,
        LogicalDeviceViewModel? device = null)
    {
        if (item is null)
            return null;

        FileSyncOperation pushOperation = null;
        SyncFile source;
        try
        {
            source = new SyncFile(item, true);
        }
        catch
        {
            return null;
        }

        if (string.IsNullOrEmpty(source.FullName))
            return null;

        string androidDest = FileHelper.ConcatPaths(targetPath, source.FullName);
        var target = new SyncFile(androidDest,
            source.IsDirectory ? FileType.Folder : FileType.File)
            { Size = source.Size };

        device ??= Data.DevicesObject.Current;
        if (device is null)
            return null;

        replacePaths ??= EmptyPathSet;
        conflictPaths ??= EmptyPathSet;

        if (conflictPaths.Count > 0)
        {
            if (!FileMergeHelper.FilterSyncTreeByConflictResolution(
                    source,
                    source.FullName,
                    replacePaths,
                    conflictPaths,
                    '/'))
            {
                return null;
            }
        }
        else if (!FileMergeHelper.FilterIdenticalPushTree(source, androidDest, device.ID))
        {
            return null;
        }

        var pushDevice = device;
        App.SafeInvoke(() =>
        {
            pushOperation = FileSyncOperation.PushFile(source, target, pushDevice, App.AppDispatcher);
            pushOperation.DropEffects = dropEffects;
            pushOperation.OriginalShellItem = originalShellItem;
            pushOperation.PropertyChanged += PushOperation_PropertyChanged;
            Data.FileOpQ.AddOperation(pushOperation);
        });

        return pushOperation;
    }

    public static void PushShellObjects(
        IEnumerable<ShellItem> items,
        string targetPath,
        DragDropEffects dropEffects = DragDropEffects.Copy,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null,
        LogicalDeviceViewModel? device = null)
        => items.ForEach(item => PushShellObject(item, targetPath, dropEffects, replacePaths: replacePaths, conflictPaths: conflictPaths, device: device));

    private static void PushOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileSyncOperation;

        if (e.PropertyName != nameof(FileOperation.Status)
            || op.Status is FileOperation.OperationStatus.Waiting
            or FileOperation.OperationStatus.InProgress)
            return;

        // If operation was cancelled or had failed - don't delete the source, but still perform cleanup
        if (op.Status is FileOperation.OperationStatus.Completed)
        {
            // Current path (and device) is where the new file was pushed to and it is not shown yet
            if (op.Device.ID == Data.DevicesObject.Current?.ID
                && op.TargetPath.ParentPath == Data.CurrentPath
                && Data.DirList.FileList.All(f => f.FullName != op.FilePath.FullName))
            {
                op.Dispatcher.Invoke(() =>
                    Data.DirList.FileList.Add(new(op.TargetPath) { ModifiedTime = op.FilePath.DateModified }));
            }

            if (op.FilePath.IsDirectory)
                ExplorerTree?.AddCreatedFolder(op.Device.ID, op.TargetPath.FullPath);

            if (op.FilePath.IsDirectory
                && op.FilePath.ShellItem is ShellFolder shellFolder)
            {
                var empty = FolderHelper.GetEmptySubfoldersRecursively(shellFolder);
                var parentPath = op.FilePath?.FullPath;
                foreach (var folder in empty)
                {
                    if (string.IsNullOrEmpty(folder.FileSystemPath) || string.IsNullOrEmpty(parentPath))
                        continue;

                    string relative = FileHelper.ExtractRelativePath(folder.FileSystemPath, parentPath).Replace('\\', '/');
                    _ = ShellFileOperation.TryMakeDir(op.Device, FileHelper.ConcatPaths(op.TargetPath.FullPath, relative));
                }
            }

            // In push we can delete the source once the operation has completed
            if (op.DropEffects is DragDropEffects.Move)
            {
                try
                {
                    if (op.FilePath.IsDirectory)
                        Directory.Delete(op.FilePath.FullPath, true);
                    else
                        File.Delete(op.FilePath.FullPath);
                }
                catch
                { }
            }
        }

        op.FilePath.ShellItem = null;
        op.PropertyChanged -= PushOperation_PropertyChanged;
    }

    // Pull where we know the actual target path
    public static void PullFiles(string targetPath = "")
    {
        Data.RaiseFocusNavigationBox(false);

        if (ActionFlags.IsAppDrive)
        {
            PullPackages(targetPath);
            return;
        }

        // Snapshot before the folder picker — SelectedFiles is a live Where(IsSelected).
        var pullItems = Data.SelectedFiles.ToList();
        if (pullItems.Count == 0)
            return;

        if (string.IsNullOrEmpty(targetPath))
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false,
                DefaultDirectory = Data.Settings.DefaultFolder,
                Title = pullItems.Count > 1
                    ? Strings.Resources.S_ITEM_DESTINATION_PLURAL
                    : string.Format(Strings.Resources.S_ITEM_DESTINATION, pullItems[0]),
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;
           
            targetPath = dialog.FileName;
            if (!Directory.Exists(targetPath) && FileHelper.GetFullName(targetPath) == pullItems[0].FullName)
                targetPath = FileHelper.GetParentPath(targetPath);
        }

        PullFiles(targetPath, pullItems, true);
    }

    public static void PullPackages(string targetPath = "")
    {
        var packages = Data.SelectedPackages.ToList();
        if (packages.Count == 0)
            return;

        if (string.IsNullOrEmpty(targetPath))
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false,
                DefaultDirectory = Data.Settings.DefaultFolder,
                Title = packages.Count > 1
                    ? Strings.Resources.S_ITEM_DESTINATION_PLURAL
                    : string.Format(Strings.Resources.S_ITEM_DESTINATION, packages[0].Name),
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            targetPath = dialog.FileName;
            if (!Directory.Exists(targetPath) && FileHelper.GetFullName(targetPath) == packages[0].Name)
                targetPath = FileHelper.GetParentPath(targetPath);
        }

        var pullItems = FileHelper.GetFilesFromTree(
            FileHelper.GetFolderTree(packages.Select(p => p.Path), false, Data.DeviceCts.Token));

        PullFiles(targetPath, pullItems, true);
    }

    public static async void PullFiles(string targetPath, IEnumerable<FileClass> pullItems, bool notify = false)
    {
        if (pullItems is null)
            return;

        // Materialize once — callers may pass a live selection query.
        var items = pullItems as IList<FileClass> ?? pullItems.ToList();
        if (items.Count == 0)
            return;

        var match = AdbRegEx.RE_WINDOWS_DRIVE_ROOT().Match(targetPath);
        var invalidFiles = items.Where(f => AdbExplorerConst.INVALID_WINDOWS_ROOT_PATHS.Contains(f.FullName)).ToList();

        if (match.Success && invalidFiles.Count > 0)
        {
            var result = await DialogService.ShowConfirmation(string.Format(Strings.Resources.S_WIN_ROOT_ILLEGAL, invalidFiles.Count),
                                                 Strings.Resources.S_WIN_ROOT_ILLEGAL_TITLE,
                                                 primaryText: Strings.Resources.S_SKIP,
                                                 icon: DialogService.DialogIcon.Exclamation,
                                                 error: DialogError.WinRootIllegalPath);

            if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
                return;

            items = items.Except(invalidFiles).ToList();
            if (items.Count == 0)
                return;
        }

        if (!Directory.Exists(targetPath))
        {
            try
            {
                Directory.CreateDirectory(targetPath);
            }
            catch (Exception e)
            {
                DialogService.ShowMessage(e.Message,
                                          Strings.Resources.S_DEST_ERR,
                                          DialogService.DialogIcon.Critical,
                                          copyToClipboard: true,
                                          error: DialogError.DestinationPathFailed);
                return;
            }
        }

        var outcome = await CopyPasteService.MergeFiles(targetPath, items);
        items = [.. outcome.Items];
        if (items.Count == 0)
            return;

        try
        {
            var ops = await Task.Run(() => GeneratePullOps(
                targetPath,
                items,
                notify,
                replacePaths: outcome.ReplaceRelativePaths,
                conflictPaths: outcome.ConflictRelativePaths).ToList());
            Data.FileOpQ.AddOperations(ops);
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message,
                                      Strings.Resources.S_DEST_ERR,
                                      DialogService.DialogIcon.Critical,
                                      copyToClipboard: true,
                                      error: DialogError.DestinationPathFailed);
        }
    }

    public static IEnumerable<FileSyncOperation> SilentPullFiles(LogicalDeviceViewModel device, string target, int maxThreads, IEnumerable<string> filesToReplace, params IEnumerable<FileClass> pullItems)
    {
        maxThreads = Math.Max(1, maxThreads);
        foreach (var item in pullItems)
        {
            if (item.Type is not FileType.Folder)
                continue;

            var syncFile = CopyPasteService.MergeFolderTree(item, target, filesToReplace);
            if (syncFile.Children.Count == 0)
                continue;

            var op = GeneratePullOp(target, syncFile, false, device);
            if (op is null)
                continue;

            op.MaxThreads = maxThreads;

            op.Start();
            yield return op;
        }
    }

    private static IEnumerable<FileSyncOperation> GeneratePullOps(
        string targetPath,
        IEnumerable<FileClass> pullItems,
        bool notify,
        LogicalDeviceViewModel? device = null,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null)
    {
        device ??= Data.DevicesObject.Current;
        var deviceId = device.ID;
        replacePaths ??= EmptyPathSet;
        conflictPaths ??= EmptyPathSet;

        foreach (var item in pullItems)
        {
            FileSyncOperation? op;
            if (ArchivePath.TryParse(item.FullPath, out var archivePath, out var internalPath, deviceId)
                && !string.IsNullOrEmpty(internalPath))
            {
                op = GenerateArchivePullOp(targetPath, item, archivePath, internalPath, notify, device);
            }
            else
            {
                op = GeneratePullOp(targetPath, item.GetSyncFile(deviceId), notify, device, replacePaths, conflictPaths);
            }

            if (op is not null)
                yield return op;
        }
    }

    private static readonly HashSet<string> EmptyPathSet = [];

    private static FileSyncOperation? GenerateArchivePullOp(
        string targetPath,
        FileClass item,
        string archivePath,
        string internalPath,
        bool notify,
        LogicalDeviceViewModel device)
    {
        string stagingRoot;
        string extractedPath;
        FolderTree[] tree;
        try
        {
            (stagingRoot, extractedPath, tree) = ArchiveExtract.ExtractSelectionForPull(
                device.ID,
                archivePath,
                internalPath,
                item.IsDirectory,
                Data.DeviceCts.Token);
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"Archive extract for pull failed: {e.Message}");
#endif
            throw;
        }

        var extractedClass = new FileClass(item.FullName, extractedPath, item.Type, size: item.Size, modifiedTime: item.ModifiedTime);
        var pullSource = new SyncFile(extractedClass, tree);
        var target = SyncFile.MergeToWindowsPath(pullSource, targetPath);

        if (!FileMergeHelper.FilterIdenticalPullTree(pullSource, target.FullPath))
        {
            ArchiveExtract.CleanupStaging(device.ID, stagingRoot);
            return null;
        }

        var fileOp = FileSyncOperation.PullFile(pullSource, target, device, App.AppDispatcher);

        // Keep UI navigation pointing at the archive member, not the temp extract path.
        fileOp.SetArchivePullSource(archivePath, internalPath, stagingRoot, item.FullPath);

        if (notify)
            fileOp.PropertyChanged += PullOperation_PropertyChanged;

        return fileOp;
    }

    private static FileSyncOperation? GeneratePullOp(
        string targetPath,
        SyncFile item,
        bool notify,
        LogicalDeviceViewModel device,
        IReadOnlySet<string>? replacePaths = null,
        IReadOnlySet<string>? conflictPaths = null)
    {
        var target = SyncFile.MergeToWindowsPath(item, targetPath);

        if (conflictPaths is { Count: > 0 })
        {
            if (!FileMergeHelper.FilterSyncTreeByConflictResolution(
                    item,
                    item.FullName,
                    replacePaths ?? EmptyPathSet,
                    conflictPaths,
                    '\\'))
            {
                return null;
            }
        }
        else if (!FileMergeHelper.FilterIdenticalPullTree(item, target.FullPath))
        {
            return null;
        }

        var fileOp = FileSyncOperation.PullFile(item, target, device, App.AppDispatcher);

        if (notify)
            fileOp.PropertyChanged += PullOperation_PropertyChanged;

        return fileOp;
    }

    private static void PullOperation_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(FileOperation.Status) || sender is not FileSyncOperation op)
            return;

        if (op.Status is FileOperation.OperationStatus.Completed)
            NativeMethods.RefreshExplorerDirectory(op.TargetPath.ParentPath);
    }

    public static async void FollowLink()
    {
        var target = Data.SelectedFiles.First().LinkTarget;

        if (string.IsNullOrEmpty(target))
            return;

        if (FileHelper.GetParentPath(target) != Data.CurrentPath)
        {
            Data.RuntimeSettings.LocationToNavigate = new(target + "/..");
        }

        await AsyncHelper.WaitUntil(() => !Data.DirList.InProgress, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(20), new());

        var file = Data.DirList.FileList.FirstOrDefault(f => f.FullPath == target);
        if (file is not null)
            Data.ItemToSelect.Value = file;
    }

    public static void EnterFolder()
    {
        if (Data.SelectedFiles?.Count() != 1)
            return;

        var file = Data.SelectedFiles.First();
        var device = ActionDevice;
        var path = device is not null
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, ActionFlags.IsArchive)
            ? ArchivePath.Join(file.FullPath, "")
            : file.FullPath;

        if (device is not null && !device.IsOpen)
        {
            Data.RuntimeSettings.PendingLocationAfterDeviceOpen = new(path);
            DeviceHelper.BrowseDeviceAction(device);
            return;
        }

        Data.RuntimeSettings.LocationToNavigate = new(path);
    }

    internal static bool CanEnterSelection(FileClass file)
        => file.IsDirectory
        || (ActionDevice is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, ActionFlags.IsArchive));

    public static void OpenApkLocation(Package apk = null)
    {
        apk ??= Data.SelectedPackages.First();

        Data.RuntimeSettings.LocationToNavigate = new(FileHelper.GetParentPath(apk.Path));
    }

    public static void ApkWebSearch()
    {
        var apk = Data.SelectedPackages.First();
        
        Network.OpenBrowserSearch(apk.Name, Data.RuntimeSettings.DefaultBrowserPath);
    }
}
