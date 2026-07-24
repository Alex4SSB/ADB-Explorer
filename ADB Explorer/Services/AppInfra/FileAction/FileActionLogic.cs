using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services.AppInfra;

internal static class FileActionLogic
{
    private static bool HasRootShell => Data.DevicesObject.Current?.HasRootShell == true;

    private static bool SelectionIsFuseProtectedAndroidRoot =>
        Data.SelectedFiles.Any(f => ShellAccessHelper.IsFuseProtectedAndroidRoot(f.FullPath));

    private static bool IsTrashDriveSelectedInDriveView()
        => Data.FileActions.IsDriveViewVisible
           && Data.RuntimeSettings.SelectedDrive?.Type is AbstractDrive.DriveType.Trash;

    private static VirtualDriveViewModel? SelectedTrashDrive()
        => Data.RuntimeSettings.SelectedDrive is VirtualDriveViewModel { Type: AbstractDrive.DriveType.Trash } trash
            ? trash
            : null;

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
            RemoveApkMessage(Data.FileActions.IsAppDrive ? pkgs : files),
            Strings.Resources.S_CONF_UNI_TITLE,
            Strings.Resources.S_UNINSTALL,
            icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
            return;

        var packageTask = await Task.Run(() =>
        {
            if (Data.FileActions.IsAppDrive)
                return pkgs.Select(pkg => pkg.Name);

            return files.Select(item => ShellFileOperation.GetPackageName(Data.DevicesObject.Current, item.FullPath));
        });

        ShellFileOperation.UninstallPackages(Data.DevicesObject.Current, packageTask, App.AppDispatcher);
    }

    public static void InstallPackages()
    {
        var packages = Data.SelectedFiles;

        ShellFileOperation.InstallPackages(Data.DevicesObject.Current, packages, App.AppDispatcher);
    }

    public static void CopyToTemp()
    {
        Data.CopyPaste.VerifyAndPaste(
            DragDropEffects.Copy,
            AdbExplorerConst.TEMP_PATH,
            Data.SelectedFiles,
            App.AppDispatcher,
            Data.DevicesObject.Current,
            Data.CurrentPath);
    }

    public static void PushPackages()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = false,
            Multiselect = true,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = Strings.Resources.S_INSTALL_APK,
        };
        dialog.Filters.Add(new(Strings.Resources.S_FILE_TYPE_APK, string.Join(';', AdbExplorerConst.INSTALL_APK.Select(name => name[1..]))));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        var shItems = dialog.FileNames.Select(ShellItem.Open);
        ShellFileOperation.PushPackages(Data.DevicesObject.Current, shItems, App.AppDispatcher);
    }

    public static void UpdateModifiedDates()
    {
        ShellFileOperation.ChangeDateFromName(Data.DevicesObject.Current, Data.SelectedFiles, App.AppDispatcher);
    }

    public static void RestoreItems()
    {
        var restoreItems = (!Data.SelectedFiles.Any() ? Data.DirList.FileList : Data.SelectedFiles).Where(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        string[] existingItems = [];
        List<FileClass> existingFiles = [];
        bool merge = false;

        var restoreTask = Task.Run(() =>
        {
            existingItems = ADBService.PathsExist(Data.DevicesObject.Current.ID, restoreItems.Select(file => file.TrashIndex.OriginalPath));
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

                ShellFileOperation.MoveItems(device: Data.DevicesObject.Current,
                                         items: restoreItems,
                                         targetPath: null,
                                         currentPath: Data.CurrentPath,
                                         fileList: Data.DirList.FileList,
                                         dispatcher: App.AppDispatcher);

                var remainingItems = Data.DirList.FileList.Except(restoreItems);
                TrashHelper.EnableRecycleButtons(remainingItems);

                // Clear all remaining files if none of them are indexed
                if (!remainingItems.Any(item => item.TrashIndex is not null))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(Data.DevicesObject.Current, remainingItems));
                }

                if (!Data.SelectedFiles.Any())
                    TrashHelper.EnableRecycleButtons();
            });
        });
    }

    public static void CopyItemPath()
    {
        var path = Data.FileActions.IsAppDrive ? Data.SelectedPackages.First().Name : Data.SelectedFiles.First().FullPath;
        Clipboard.SetText(path);
    }

    public static async Task CreateNewItem(FileClass file, string newName = null)
    {
        if (!string.IsNullOrEmpty(newName))
            file.UpdatePath($"{Data.CurrentPath}{(Data.CurrentPath == "/" ? "" : "/")}{newName}");

        if (Data.Settings.ShowExtensions)
            file.UpdateType();

        try
        {
            var device = Data.DevicesObject.Current;
            if (ArchivePath.TryParse(file.FullPath, out var archivePath, out var internalPath, device.ID)
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
            Data.DirList.FileList.Remove(file);
            return;
        }

        file.IsTemp = false;
        file.ModifiedTime = DateTime.Now;
        if (file.Type is FileType.File)
            file.Size = 0;

        var index = Data.DirList.FileList.IndexOf(file);
        Data.DirList.FileList.Remove(file);
        Data.DirList.FileList.Insert(index, file);
        Data.ItemToSelect.Value = file;
    }

    public static void IsPasteEnabled()
    {
        // Do not update if drag is active
        if (Data.CopyPaste.IsDrag)
            return;

        // Explorer view AND source is clipboard
        if (Data.FileActions.IsPasteStateVisible && Data.CopyPaste.Files.Length > 0)
        {
            Data.FileActions.CutItemsCount.Value = Data.CopyPaste.Files.Length.ToString();
        }
        else
        {
            Data.FileActions.CutItemsCount.Value = "";
            Data.FileActions.IsCopyState.Value = false;
            Data.FileActions.IsCutState.Value = false;

            Data.FileActions.PasteEnabled = false;
            Data.FileActions.IsKeyboardPasteEnabled = false;

            return;
        }

        string stringFormat;
        if (Data.CopyPaste.Files.Length > 1)
        {
            if (Data.FileActions.IsAppDrive)
            {
                stringFormat = Strings.Resources.S_DRAG_INSTALL_MULTIPLE;
            }
            else
            {
                stringFormat = Data.CopyPaste.PasteState is DragDropEffects.Move
                    ? Strings.Resources.S_PASTE_PLURAL_CUT_ITEMS
                    : Strings.Resources.S_PASTE_PLURAL_COPIED_ITEMS;
            }

            Data.FileActions.PasteDescription.Value = string.Format(stringFormat, Data.CopyPaste.Files.Length);
        }
        else
        {
            if (Data.FileActions.IsAppDrive)
            {
                stringFormat = string.Format(Strings.Resources.S_DRAG_INSTALL_SINGLE, Data.CopyPaste.CurrentFiles.FirstOrDefault()?.NoExtName);
            }
            else
            {
                stringFormat = Data.CopyPaste.PasteState is DragDropEffects.Move
                    ? Strings.Resources.S_PASTE_ONE_CUT_ITEM
                    : Strings.Resources.S_PASTE_ONE_COPIED_ITEM;
            }

            Data.FileActions.PasteDescription.Value = stringFormat;
        }

        if (Data.FileActions.IsAppDrive)
        {
            Data.FileActions.PasteEnabled = FileHelper.AllFilesAreApks(Data.CopyPaste.Files);
            Data.FileActions.IsKeyboardPasteEnabled = false;
        }
        else
        {
            Data.FileActions.PasteEnabled = EnableUiPaste();
            Data.FileActions.IsKeyboardPasteEnabled = EnableKeyboardPaste();
        }
    }

  public static bool EnableUiPaste()
    {
        if (Data.CurrentDrive?.Restrictions.ReadOnly is true)
            return false;

        string[] files = Data.CopyPaste.Files;
        if (Data.CopyPaste.IsWindows
            && Data.CopyPaste.IsVirtual
            && Data.CopyPaste.Descriptors.Length == files.Length)
        {
            files = [.. Data.CopyPaste.Descriptors.Select(d => d.Name)];
        }

        Data.FileActions.IsPastingInDescendant = files.Length == 1
            && FileHelper.RelationFrom(files[0], Data.CurrentPath) is RelationType.Descendant or RelationType.Self;

        if (Data.FileActions.IsPastingInDescendant)
            return false;

        var selected = Data.SelectedFiles?.Count();

        string targetPath;
        if (selected == 1)
        {
            var targetFile = Data.SelectedFiles.First();
            targetPath = targetFile.IsLink ? targetFile.LinkTarget : targetFile.FullPath;
        }
        else
        {
            targetPath = Data.CurrentPath;
        }

        if (!IsPasteIntoTargetAllowed(targetPath))
            return false;

        UpdatePastingRestrictions(targetPath, files);

        if (Data.FileActions.IsPastingIllegalNaming || Data.FileActions.IsPastingConflictingNames)
            return false;

        switch (selected)
        {
            case 0:
                Data.FileActions.IsPastingInDescendant = Data.CopyPaste.ParentFolder == Data.CurrentPath
                    && Data.CopyPaste.PasteState is DragDropEffects.Move;

                break;
            case 1:
                var item = Data.SelectedFiles.First();
                if (!item.IsDirectory)
                    return false;

                Data.FileActions.IsPastingInDescendant = (files.Length == 1 && files[0] == item.FullPath)
                    || (Data.CopyPaste.ParentFolder == item.FullPath);

                break;
            default:
                return false;
        }

        return !Data.FileActions.IsPastingInDescendant
            && DriveHelper.IsModificationAllowedAt(targetPath, Data.DevicesObject?.Current?.ID ?? "");
    }

    public static bool EnableKeyboardPaste()
    {
        if (Data.CurrentDrive?.Restrictions.ReadOnly is true)
            return false;

        string[] files = Data.CopyPaste.Files;
        if (Data.CopyPaste.IsWindows
            && Data.CopyPaste.IsVirtual
            && Data.CopyPaste.Descriptors.Length == files.Length)
        {
            files = [.. Data.CopyPaste.Descriptors.Select(d => d.Name)];
        }

        Data.FileActions.IsPastingInDescendant = files.Length == 1
            && FileHelper.RelationFrom(files[0], Data.CurrentPath) is RelationType.Descendant or RelationType.Self;

        if (Data.FileActions.IsPastingInDescendant)
            return false;

        var selected = Data.SelectedFiles?.Count() > 1 ? 0 : Data.SelectedFiles?.Count();

        string targetPath;
        if (selected == 1)
        {
            var targetFile = Data.SelectedFiles.First();
            targetPath = targetFile.IsLink ? targetFile.LinkTarget : targetFile.FullPath;
        }
        else
        {
            targetPath = Data.CurrentPath;
        }

        if (!IsPasteIntoTargetAllowed(targetPath))
            return false;

        UpdatePastingRestrictions(targetPath, files);

        if (Data.FileActions.IsPastingIllegalNaming || Data.FileActions.IsPastingConflictingNames)
            return false;

        switch (selected)
        {
            case 0:
                Data.FileActions.IsPastingInDescendant = Data.CopyPaste.ParentFolder == Data.CurrentPath
                    && Data.CopyPaste.PasteState is DragDropEffects.Move;

                break;
            case 1:
                // When duplicating a file multiple times using the keyboard, the selection is the previous copy
                if (Data.CopyPaste.PasteState is DragDropEffects.Copy && Data.DirList.FileList.Any(f => f.FullPath == files[0]))
                    return DriveHelper.IsModificationAllowedAt(targetPath, Data.DevicesObject?.Current?.ID ?? "");

                var item = Data.SelectedFiles.First();
                if (!item.IsDirectory)
                    return false;

                Data.FileActions.IsPastingInDescendant = (files.Length == 1 && files[0] == item.FullPath)
                    || (Data.CopyPaste.ParentFolder == item.FullPath);

                break;
            default:
                return false;
        }

        return !Data.FileActions.IsPastingInDescendant
            && DriveHelper.IsModificationAllowedAt(targetPath, Data.DevicesObject?.Current?.ID ?? "");
    }

    private static bool IsPasteIntoTargetAllowed(string targetPath)
    {
        var deviceId = Data.DevicesObject?.Current?.ID ?? "";
        if (!ArchivePath.IsArchivePath(targetPath, deviceId))
            return true;

        return ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId);
    }

    public static DragDropEffects EnableDropPaste(FileClass target = null)
    {
        if (!Data.CopyPaste.CurrentFiles.Any())
            return DragDropEffects.None;

        var pastingInDescendant = Data.CopyPaste.DragFiles.Length == 1
            && Data.CopyPaste.CurrentFiles.First().Relation(Data.CurrentPath) is RelationType.Descendant or RelationType.Self;

        if (pastingInDescendant || Data.FileActions.IsRecycleBin)
            return DragDropEffects.None;

        if (FileHelper.RelationFrom(Data.CopyPaste.DragParent, AdbExplorerConst.RECYCLE_PATH) is RelationType.Self or RelationType.Ancestor)
            return DragDropEffects.Move;

        string targetPath = target switch
        {
            null => Data.CurrentPath,
            _ when target.IsLink => target.LinkTarget,
            _ => target.FullPath,
        };

        if (!DriveHelper.IsModificationAllowedAt(targetPath, Data.DevicesObject.Current?.ID ?? ""))
            return DragDropEffects.None;

        var deviceId = Data.DevicesObject.Current?.ID;
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
            && DriveHelper.GetCurrentDrive(targetPath)?.Restrictions.NoSymbolicLinks is not true
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
            if (!target.IsDirectory)
                return DragDropEffects.None;

            pastingInDescendant = (Data.CopyPaste.DragFiles.Length == 1 && Data.CopyPaste.CurrentFiles.First().FullPath == target.FullPath)
                || (Data.CopyPaste.DragParent == target.FullPath);
        }

        if (pastingInDescendant)
            return DragDropEffects.None;

        // Archive extract is copy-only.
        return fromArchive ? result : result | DragDropEffects.Move;
    }

    private static void UpdatePastingRestrictions(string targetPath, string[] files)
    {
        var restrictions = DriveHelper.GetCurrentDrive(targetPath)?.Restrictions ?? DriveRestrictions.None;

        if (Data.FileActions.IsAppDrive)
        {
            Data.FileActions.IsPastingIllegalNaming = Data.CopyPaste.IsSelf
                && (DriveHelper.GetCurrentDrive(files[0])?.Restrictions.RestrictedNaming is true);
            return;
        }

        Data.FileActions.IsPastingIllegalNaming = restrictions.RestrictedNaming
            && !FileHelper.FileNameLegal(files.Select(FileHelper.GetFullName), FileHelper.RenameTarget.RestrictedNaming);

        Data.FileActions.IsPastingConflictingNames = restrictions.CaseInsensitiveNames
            && files.Distinct(StringComparer.InvariantCultureIgnoreCase).Count() != files.Length;
    }

    public static void PasteFiles(IEnumerable<FileClass> selectedFiles, bool isLink = false)
    {
        Data.CopyPaste.AcceptDataObject(Clipboard.GetDataObject(), selectedFiles, isLink);

        IsPasteEnabled();
    }

    public static void CutItems(bool isCopy = false)
    {
        if (Data.FileActions.IsAppDrive)
            CopyPackages(Data.SelectedPackages);
        else
            CutFiles(Data.SelectedFiles, isCopy);
    }

    public static void CopyPackages(IEnumerable<Package> items)
    {
        Data.FileActions.CopyEnabled = false;
        Data.FileActions.CutEnabled = true;

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

        Data.FileActions.CopyEnabled = !isCopy;
        Data.FileActions.CutEnabled = isCopy;

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

    public static void Rename(TextBox textBox)
    {
        if (textBox.DataContext is not FileClass file)
            return;

        var vm = file.ActiveViewModel;
        var name = FileHelper.DisplayName(textBox);

        if (!vm.IsRenameUnixLegal
            || (Data.CurrentDrive?.Restrictions.RestrictedNaming is true && !vm.IsRenameNamingLegal)
            || !vm.IsRenameUnique)
        {
            return;
        }

        if (file.IsTemp)
        {
            if (string.IsNullOrEmpty(textBox.Text))
            {
                Data.DirList.FileList.Remove(file);
                return;
            }
            
            _ = CreateNewItem(file, textBox.Text);
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

    public static async void DeleteFiles(bool? permanent = null)
    {
        permanent ??= Keyboard.Modifiers is ModifierKeys.Shift;

        var emptyTrashFromDriveView = IsTrashDriveSelectedInDriveView() && !Data.SelectedFiles.Any();
        var emptyingRecycleBin = (Data.FileActions.IsRecycleBin && !Data.SelectedFiles.Any()) || emptyTrashFromDriveView;

        List<FileClass> itemsToDelete;
        if (emptyingRecycleBin)
        {
            itemsToDelete = emptyTrashFromDriveView
                ? TrashHelper.GetRecycleBinItems()
                : [.. Data.DirList.FileList.Where(f => f.Extension != AdbExplorerConst.RECYCLE_INDEX_SUFFIX)];
        }
        else
        {
            itemsToDelete = [.. HasRootShell
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

        if (!Data.FileActions.IsRecycleBin && !emptyTrashFromDriveView && Data.Settings.EnableRecycle && !permanent.Value)
        {
            // Archive members cannot be moved to the recycle bin — always permanent-delete them.
            var deviceId = Data.DevicesObject.Current?.ID ?? "";
            if (itemsToDelete.Any(f => ArchivePath.IsArchivePath(f.FullPath, deviceId)))
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

        if (!Data.FileActions.IsRecycleBin && !emptyTrashFromDriveView && Data.Settings.EnableRecycle && !permanent.Value)
        {
            await ShellFileOperation.MakeDir(Data.DevicesObject.Current, AdbExplorerConst.RECYCLE_PATH);

            ShellFileOperation.MoveItems(Data.DevicesObject.Current,
                                         itemsToDelete,
                                         AdbExplorerConst.RECYCLE_PATH,
                                         Data.CurrentPath,
                                         Data.DirList.FileList,
                                         App.AppDispatcher);
        }
        else
        {
            ShellFileOperation.DeleteItems(Data.DevicesObject.Current, itemsToDelete, App.AppDispatcher);

            if (Data.FileActions.IsRecycleBin)
            {
                var remainingItems = Data.DirList.FileList.Except(itemsToDelete);
                TrashHelper.EnableRecycleButtons(remainingItems);

                // Clear all remaining files if none of them are indexed
                if (!remainingItems.Any(item => item.TrashIndex is not null))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(Data.DevicesObject.Current, remainingItems));
                }
            }
            else if (emptyTrashFromDriveView)
            {
                var indexPaths = ADBService.FindFilesInPath(Data.DevicesObject.Current.ID,
                                                            AdbExplorerConst.RECYCLE_PATH,
                                                            includeNames: ["*" + AdbExplorerConst.RECYCLE_INDEX_SUFFIX]);
                if (indexPaths.Length > 0)
                    ShellFileOperation.SilentDelete(Data.DevicesObject.Current, indexPaths);

                SelectedTrashDrive()?.SetItemsCount(0);
            }
        }
    }

    public static void Refresh()
    {
        if (Data.FileActions.IsAppDrive)
        {
            UpdatePackages(true);
            return;
        }

        if (Data.FileActions.IsDriveViewVisible)
        {
            Data.RuntimeSettings.LocationToNavigate = new(Navigation.SpecialLocation.DriveView);
            return;
        }

        Data.RuntimeSettings.LocationToNavigate = new(Data.CurrentPath);
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

                if (App.AppDispatcher is not null && await Data.DevicesObject.Current?.UpdateDrives(result.Drives, App.AppDispatcher, asyncClassify))
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

    public static void UpdatePackages(bool updateExplorer = false, CancellationToken cancellationToken = default)
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
                Data.Packages = t.Result;
                if (updateExplorer)
                    App.Services.GetService<ExplorerViewModel>().ExplorerSource = Data.Packages;

                if (!updateExplorer && Data.DevicesObject.Current is not null)
                {
                    var package = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
                    ((VirtualDriveViewModel)package)?.SetItemsCount(Data.Packages.Count);
                }

                Data.FileActions.ListingInProgress = false;
            });
        });
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
            Data.FileActions.RenameEnabled =
            Data.FileActions.HomeEnabled =
            Data.FileActions.NewEnabled =
            Data.FileActions.PasteEnabled =
            Data.FileActions.IsUninstallVisible.Value =
            Data.FileActions.CutEnabled =
            Data.FileActions.CopyEnabled =
            Data.FileActions.IsExplorerVisible =
            Data.FileActions.PackageActionsEnabled =
            Data.FileActions.IsCopyItemPathEnabled =
            Data.FileActions.UpdateModifiedEnabled =
            Data.FileActions.IsFollowLinkEnabled =
            Data.RuntimeSettings.IsExplorerLoaded =
            Data.FileActions.ParentEnabled = false;

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

    public static void UpdateFileActions()
    {
        Data.FileActions.IsApkActionsVisible.Value = Data.Settings.EnableApk && Data.DevicesObject?.Current;
        Data.FileActions.PushPackageEnabled = Data.FileActions.IsApkActionsVisible && Data.DevicesObject?.Current?.Type is not DeviceType.Recovery;

        Data.FileActions.UninstallPackageEnabled = Data.FileActions.IsAppDrive && Data.SelectedPackages.Any();
        Data.FileActions.ContextPushPackagesEnabled = Data.FileActions.IsAppDrive && !Data.SelectedPackages.Any();

        Data.FileActions.IsRefreshEnabled = Data.FileActions.IsDriveViewVisible || Data.FileActions.IsExplorerVisible;
        Data.FileActions.IsCopyCurrentPathEnabled = Data.FileActions.IsExplorerVisible && !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive;

        Data.FileActions.IsOpenApkLocationEnabled = Data.FileActions.IsAppDrive && Data.SelectedPackages.Count() == 1;
        Data.FileActions.IsApkWebSearchEnabled = Data.FileActions.IsOpenApkLocationEnabled && !string.IsNullOrEmpty(Data.RuntimeSettings.DefaultBrowserPath);

        Data.FileActions.IsRegularItem = !Data.SelectedFiles.Any() || HasRootShell
            || Data.SelectedFiles.AnyAll(item => item.Type is FileType.File or FileType.Folder);

        Data.FileActions.IsSingleFolder = Data.SelectedFiles.Count() == 1
            && CanEnterSelection(Data.SelectedFiles.First());

        Data.FileActions.IsFollowLinkEnabled = !Data.FileActions.IsRecycleBin
                                               && Data.SelectedFiles.Count() == 1
                                               && Data.SelectedFiles.First().IsLink
                                               && Data.SelectedFiles.First().Type is not FileType.BrokenLink;

        var restrictions = Data.CurrentDrive?.Restrictions ?? DriveRestrictions.None;
        var deviceId = Data.DevicesObject?.Current?.ID;
        if (deviceId is not null && Data.DirList?.CurrentPath is { } currentPath)
            Data.FileActions.IsArchive = ArchivePath.IsArchivePath(currentPath, deviceId);

        var isWritable = restrictions.ReadOnly is not true
            && Data.DirList?.CurrentLocation?.CanWriteLocation == true;
        var canPasteIntoTar = deviceId is not null
            && ArchiveHelper.CanPasteIntoArchive(Data.CurrentPath ?? "", deviceId);
        var isExplorerFolder = Data.FileActions.IsExplorerVisible
            && !Data.FileActions.IsRecycleBin
            && !Data.FileActions.IsAppDrive
            && !Data.FileActions.IsArchive;

        Data.FileActions.IsCurrentLocationReadOnly = (isExplorerFolder || canPasteIntoTar) && !isWritable;
        Data.FileActions.IsSelectionFuseProtectedAndroidRoot = Data.SelectedFiles.Any()
            && SelectionIsFuseProtectedAndroidRoot;

        // Push into modifiable tar is allowed; New File/Folder inside modifiable tar is allowed.
        Data.FileActions.PushFilesFoldersEnabled = isWritable && (isExplorerFolder || canPasteIntoTar);
        Data.FileActions.NewEnabled = isWritable && (isExplorerFolder || canPasteIntoTar);
        Data.FileActions.IsNewMenuVisible.Value = Data.FileActions.IsExplorerVisible
            && !Data.FileActions.IsRecycleBin
            && !Data.FileActions.IsAppDrive
            && Data.FileActions.NewEnabled;

        if (Data.FileActions.IsRecycleBin)
        {
            TrashHelper.EnableRecycleButtons(Data.SelectedFiles.Any() ? Data.SelectedFiles : Data.DirList.FileList);
        }
        else if (SelectedTrashDrive() is { ItemsCount: > 0 })
        {
            Data.FileActions.DeleteEnabled = true;
            Data.FileActions.RestoreEnabled = false;
        }
        else
        {
            Data.FileActions.DeleteEnabled = isWritable
                && !SelectionIsFuseProtectedAndroidRoot
                && Data.SelectedFiles.Any() && Data.FileActions.IsRegularItem
                && (!Data.FileActions.IsFollowLinkEnabled || HasRootShell)
                && (!Data.FileActions.IsArchive || canPasteIntoTar);

            Data.FileActions.RestoreEnabled = false;
        }

        Data.FileActions.PullDescription.Value = Data.FileActions.IsFollowLinkEnabled ? Strings.Resources.S_PULL_ACTION_LINK : Strings.Resources.S_PULL_ACTION;
        if (Data.FileActions.IsRecycleBin)
        {
            Data.FileActions.DeleteDescription.Value = Data.SelectedFiles.Any()
                ? Strings.Resources.S_PERM_DEL
                : Strings.Resources.S_EMPTY_TRASH;
        }
        else if (IsTrashDriveSelectedInDriveView())
        {
            Data.FileActions.DeleteDescription.Value = Strings.Resources.S_EMPTY_TRASH;
        }
        else
        {
            Data.FileActions.DeleteDescription.Value = Strings.Resources.S_DELETE_ACTION;
            Data.FileActions.ContextDeleteDescription.Value =
                Data.FileActions.IsArchive || Keyboard.Modifiers is ModifierKeys.Shift
                    ? Strings.Resources.S_PERM_DEL
                    : Strings.Resources.S_DELETE_ACTION;
        }

        Data.FileActions.RestoreDescription.Value = Data.FileActions.IsRecycleBin && !Data.SelectedFiles.Any() ? Strings.Resources.S_RESTORE_ALL : Strings.Resources.S_RESTORE_ACTION;

        Data.FileActions.IsSelectionIllegalOnWindows = Data.SelectedFiles.Any() && !FileHelper.FileNameLegal(Data.SelectedFiles, FileHelper.RenameTarget.Windows);
        Data.FileActions.IsSelectionIllegalNaming = !Data.FileActions.IsRecycleBin
            && !Data.FileActions.IsAppDrive
            && !Data.FileActions.IsArchive
            && Data.SelectedFiles.Any()
            && !FileHelper.FileNameLegal(Data.SelectedFiles, FileHelper.RenameTarget.RestrictedNaming);
        Data.FileActions.IsSelectionIllegalOnWinRoot = Data.SelectedFiles.Any() && !FileHelper.FileNameLegal(Data.SelectedFiles, FileHelper.RenameTarget.WinRoot);
        Data.FileActions.IsSelectionConflictingNames = restrictions.CaseInsensitiveNames
            && Data.SelectedFiles.Select(f => f.FullName).Distinct(StringComparer.InvariantCultureIgnoreCase).Count() != Data.SelectedFiles.Count();

        // Pull from archive extracts selected members to /data/local/tmp then pulls (same as PrepareDescriptors).
        Data.FileActions.PullEnabled = !Data.FileActions.IsRecycleBin
                                       && Data.SelectedFiles.AnyAll(f => f.Type is not FileType.BrokenLink)
                                       && Data.FileActions.IsRegularItem
                                       && !Data.FileActions.IsSelectionIllegalOnWindows
                                       && !Data.FileActions.IsSelectionIllegalNaming
                                       && !Data.FileActions.IsSelectionConflictingNames
                                       && (!Data.FileActions.IsArchive
                                           || Data.SelectedFiles.AnyAll(f =>
                                               ArchivePath.TryParse(f.FullPath, out _, out var inner, deviceId)
                                               && !string.IsNullOrEmpty(inner)));

        Data.FileActions.ContextPushEnabled = isWritable
            && !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive
            && (!Data.FileActions.IsArchive || canPasteIntoTar)
            && (!Data.SelectedFiles.Any() || (Data.SelectedFiles.Count() == 1 && Data.SelectedFiles.First().IsDirectory));

        Data.FileActions.RenameEnabled = isWritable
                                         && (!Data.FileActions.IsArchive || canPasteIntoTar)
                                         && !SelectionIsFuseProtectedAndroidRoot
                                         && !Data.FileActions.IsRecycleBin
                                         && Data.SelectedFiles.Count() == 1
                                         && Data.FileActions.IsRegularItem
                                         && (!Data.FileActions.IsFollowLinkEnabled || HasRootShell);

        var allSelectedAreCut = Data.CopyPaste.IsSelf
                                && Data.CopyPaste.Files.AnyAll(item => Data.SelectedFiles.Any(f => f.FullPath == item))
                                && Data.CopyPaste.Files.Length == Data.SelectedFiles.Count();
        
        // Cut from archive is not supported (extract is copy-only).
        Data.FileActions.CutEnabled = isWritable
                                      && !Data.FileActions.IsArchive
                                      && !SelectionIsFuseProtectedAndroidRoot
                                      && Data.SelectedFiles.AnyAll(f => f.Type is not FileType.BrokenLink)
                                      && !(allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Move)
                                      && Data.FileActions.IsRegularItem
                                      && (!Data.FileActions.IsFollowLinkEnabled || HasRootShell);

        if (Data.FileActions.IsAppDrive)
        {
            Data.FileActions.CopyEnabled = Data.SelectedPackages.Any();
        }
        else
        {
            Data.FileActions.CopyEnabled = Data.SelectedFiles.AnyAll(f => f.Type is not FileType.BrokenLink)
                                           && !(allSelectedAreCut && Data.CopyPaste.PasteState is DragDropEffects.Copy)
                                           && Data.FileActions.IsRegularItem
                                           && !Data.FileActions.IsRecycleBin;
        }
        
        IsPasteEnabled();

        // APK enabled in settings
        // All selected files are installable
        // Not in trash
        // If recovery, only enabled outside temp drive (to enable copy to temp, but install is disabled, even in temp drive)
        Data.FileActions.PackageActionsEnabled = Data.Settings.EnableApk
                                                 && Data.SelectedFiles.AnyAll(file => file.IsInstallApk)
                                                 && !Data.FileActions.IsRecycleBin
                                                 && !(Data.DevicesObject?.Current?.Type is DeviceType.Recovery
                                                 && Data.FileActions.IsTemp);

        Data.FileActions.IsCopyItemPathEnabled = Data.FileActions.IsAppDrive
            ? Data.SelectedPackages.Count() == 1
            : Data.SelectedFiles.Count() == 1 && !Data.FileActions.IsRecycleBin;

        Data.FileActions.ContextNewEnabled = isWritable
            && !Data.SelectedFiles.Any() && !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive
            && (!Data.FileActions.IsArchive || canPasteIntoTar);

        Data.FileActions.SubmenuUninstallEnabled = Data.CurrentDrive?.Restrictions.NoApkInstall is not true
            && Data.SelectedFiles.AnyAll(file => file.IsInstallApk)
            && Data.DevicesObject?.Current?.Type is not DeviceType.Recovery;

        Data.FileActions.UpdateModifiedEnabled = isWritable
            && !Data.FileActions.IsRecycleBin
            && Data.SelectedFiles.AnyAll(file => file.Type is FileType.File && !file.IsApk && !file.IsLink);

        Data.FileActions.IsPasteLinkEnabled = Data.CurrentDrive?.Restrictions.NoSymbolicLinks is not true
            && HasRootShell
            && Data.CopyPaste.Files.Length == 1
            && Data.CopyPaste.IsSelf
            && Data.CopyPaste.PasteState is DragDropEffects.Copy
            && (!Data.SelectedFiles.Any() ||
            (Data.SelectedFiles.Count() == 1 && Data.SelectedFiles.First().IsDirectory));

        Data.FileActions.InstallPackageEnabled = Data.DevicesObject?.Current?.Type is not DeviceType.Recovery
            && Data.CurrentDrive?.Restrictions.NoApkInstall is not true;

        if (!Data.CopyPaste.IsDrag)
            Data.RuntimeSettings.FilterActions = true;
    }

    public static void PushItems(bool isFolderPicker, bool isContextMenu)
    {
        Data.RaiseFocusNavigationBox(false);

        string targetPath, targetName = "";
        string title = "";
        if (isContextMenu && Data.SelectedFiles.Count() == 1)
        {
            targetPath = Data.SelectedFiles.First().FullPath;
            targetName = Data.SelectedFiles.First().FullName;

            title = isFolderPicker
                ? Strings.Resources.S_SELECT_FOLDER_PUSH_DESTINATION
                : Strings.Resources.S_SELECT_FILE_PUSH_DESTINATION;
        }
        else
        {
            targetPath = Data.CurrentPath;

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

    public static FileSyncOperation PushShellObject(ShellItem item, string targetPath, DragDropEffects dropEffects = DragDropEffects.Copy, ShellItem originalShellItem = null)
    {
        FileSyncOperation pushOperation = null;
        var source = new SyncFile(item, true);
        var target = new SyncFile(FileHelper.ConcatPaths(targetPath, source.FullName),
            source.IsDirectory ? FileType.Folder : FileType.File)
            { Size = source.Size };

        App.SafeInvoke(() =>
        {
            pushOperation = FileSyncOperation.PushFile(source, target, Data.DevicesObject.Current, App.AppDispatcher);
            pushOperation.DropEffects = dropEffects;
            pushOperation.OriginalShellItem = originalShellItem;
            pushOperation.PropertyChanged += PushOperation_PropertyChanged;
            Data.FileOpQ.AddOperation(pushOperation);
        });

        return pushOperation;
    }

    public static void PushShellObjects(IEnumerable<ShellItem> items, string targetPath, DragDropEffects dropEffects = DragDropEffects.Copy)
        => items.ForEach(item => PushShellObject(item, targetPath, dropEffects));

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
            if (op.Device.ID == Data.DevicesObject.Current.ID
                && op.TargetPath.ParentPath == Data.CurrentPath
                && Data.DirList.FileList.All(f => f.FullName != op.FilePath.FullName))
            {
                op.Dispatcher.Invoke(() =>
                    Data.DirList.FileList.Add(new(op.TargetPath) { ModifiedTime = op.FilePath.DateModified }));
            }

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

        var pullItems = Data.SelectedFiles;

        if (string.IsNullOrEmpty(targetPath))
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false,
                DefaultDirectory = Data.Settings.DefaultFolder,
                Title = pullItems.Count() > 1
                    ? Strings.Resources.S_ITEM_DESTINATION_PLURAL
                    : string.Format(Strings.Resources.S_ITEM_DESTINATION, pullItems.First()),
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;
           
            targetPath = dialog.FileName;
            if (!Directory.Exists(targetPath) && FileHelper.GetFullName(targetPath) == pullItems.First().FullName)
                targetPath = FileHelper.GetParentPath(targetPath);
        }

        PullFiles(targetPath, pullItems, true);
    }

    public static async void PullFiles(string targetPath, IEnumerable<FileClass> pullItems, bool notify = false)
    {
        if (pullItems is null || !pullItems.Any())
            return;

        var match = AdbRegEx.RE_WINDOWS_DRIVE_ROOT().Match(targetPath);
        var invalidFiles = pullItems.Where(f => AdbExplorerConst.INVALID_WINDOWS_ROOT_PATHS.Contains(f.FullName));

        if (match.Success && invalidFiles.Any())
        {
            var result = await DialogService.ShowConfirmation(string.Format(Strings.Resources.S_WIN_ROOT_ILLEGAL, invalidFiles.Count()),
                                                 Strings.Resources.S_WIN_ROOT_ILLEGAL_TITLE,
                                                 primaryText: Strings.Resources.S_SKIP,
                                                 icon: DialogService.DialogIcon.Exclamation,
                                                 error: DialogError.WinRootIllegalPath);

            if (result.Item1 is not Wpf.Ui.Controls.ContentDialogResult.Primary)
                return;

            pullItems = pullItems.Except(invalidFiles);
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

        // MergeFiles expects source paths; for archives pass a Windows-style path under target for conflict check.
        var conflictSources = pullItems.Select(f =>
            ArchivePath.IsArchivePath(f.FullPath, Data.DevicesObject?.Current?.ID)
                ? FileHelper.ConcatPaths(targetPath, f.FullName, '\\')
                : f.FullPath).ToList();

        var files = await CopyPasteService.MergeFiles(conflictSources, targetPath);
        if (files.Count() < pullItems.Count())
        {
            var allowedNames = files.Select(FileHelper.GetFullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
            pullItems = pullItems.Where(f =>
                ArchivePath.IsArchivePath(f.FullPath, Data.DevicesObject?.Current?.ID)
                    ? allowedNames.Contains(f.FullName)
                    : files.Contains(f.FullPath));
        }

        try
        {
            var ops = await Task.Run(() => GeneratePullOps(targetPath, pullItems, notify).ToList());
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

    public static IEnumerable<FileSyncOperation> SilentPullFiles(LogicalDeviceViewModel device, string target, bool disableParallel, IEnumerable<string> filesToReplace, params IEnumerable<FileClass> pullItems)
    {
        foreach (var item in pullItems)
        {
            if (item.Type is not FileType.Folder)
                continue;

            var syncFile = CopyPasteService.MergeFolderTree(item, target, filesToReplace);
            if (syncFile.Children.Count == 0)
                continue;

            var op = GeneratePullOp(target, syncFile, false, device);
            if (disableParallel)
                op.MaxThreads = 1;

            op.Start();
            yield return op;
        }
    }

    private static IEnumerable<FileSyncOperation> GeneratePullOps(string targetPath, IEnumerable<FileClass> pullItems, bool notify, LogicalDeviceViewModel? device = null)
    {
        device ??= Data.DevicesObject.Current;
        var deviceId = device.ID;

        foreach (var item in pullItems)
        {
            if (ArchivePath.TryParse(item.FullPath, out var archivePath, out var internalPath, deviceId)
                && !string.IsNullOrEmpty(internalPath))
            {
                yield return GenerateArchivePullOp(targetPath, item, archivePath, internalPath, notify, device);
            }
            else
            {
                yield return GeneratePullOp(targetPath, item.GetSyncFile(), notify, device);
            }
        }
    }

    private static FileSyncOperation GenerateArchivePullOp(
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
        var fileOp = FileSyncOperation.PullFile(pullSource, target, device, App.AppDispatcher);

        // Keep UI navigation pointing at the archive member, not the temp extract path.
        fileOp.SetArchivePullSource(archivePath, internalPath, stagingRoot, item.FullPath);

        if (notify)
            fileOp.PropertyChanged += PullOperation_PropertyChanged;

        return fileOp;
    }

    private static FileSyncOperation GeneratePullOp(string targetPath, SyncFile item, bool notify, LogicalDeviceViewModel device)
    {
        var target = SyncFile.MergeToWindowsPath(item, targetPath);
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
        var path = Data.DevicesObject.Current is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, Data.FileActions.IsArchive)
            ? ArchivePath.Join(file.FullPath, "")
            : file.FullPath;

        Data.RuntimeSettings.LocationToNavigate = new(path);
    }

    internal static bool CanEnterSelection(FileClass file)
        => file.IsDirectory
        || (Data.DevicesObject.Current is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, Data.FileActions.IsArchive));

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
