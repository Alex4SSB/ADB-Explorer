using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.ViewModels;
using System.IO;
using static ADB_Explorer.Converters.FileTypeClass;

namespace ADB_Explorer.Services.AppInfra;

internal static class FileActionLogic
{
    public static async void UninstallPackages()
    {
        var pkgs = Data.SelectedPackages;
        var files = Data.SelectedFiles;

        var result = await DialogService.ShowConfirmation(
        Strings.S_REM_APK(Data.FileActions.IsAppDrive ? pkgs : files),
        Strings.S_CONF_UNI_TITLE,
            "Uninstall",
        icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 is not ContentDialogResult.Primary)
            return;

        var packageTask = await Task.Run(() =>
        {
            if (Data.FileActions.IsAppDrive)
                return pkgs.Select(pkg => pkg.Name);

            return files.Select(item => ShellFileOperation.GetPackageName(Data.CurrentADBDevice, item.FullPath));
        });

        ShellFileOperation.UninstallPackages(Data.CurrentADBDevice, packageTask, App.Current.Dispatcher, Data.Packages);
    }

    public static void InstallPackages()
    {
        var packages = Data.SelectedFiles;

        Task.Run(() => ShellFileOperation.InstallPackages(Data.CurrentADBDevice, packages, App.Current.Dispatcher));
    }

    public static void CopyToTemp()
    {
        _ = ShellFileOperation.MoveItems(true,
                                         AdbExplorerConst.TEMP_PATH,
                                         Data.SelectedFiles,
                                         FileHelper.DisplayName(Data.SelectedFiles.First()),
                                         Data.DirList.FileList,
                                         App.Current.Dispatcher,
                                         Data.CurrentADBDevice,
                                         Data.CurrentPath);
    }

    public static void PushPackages()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = false,
            Multiselect = true,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = Strings.S_INSTALL_APK,
        };
        dialog.Filters.Add(new("Android Package", string.Join(';', AdbExplorerConst.INSTALL_APK.Select(name => name[1..]))));

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        Task.Run(() => ShellFileOperation.PushPackages(Data.CurrentADBDevice, dialog.FilesAsShellObject, App.Current.Dispatcher, Data.FileActions.IsAppDrive));
    }

    public static void UpdateModifiedDates()
    {
        var items = Data.SelectedFiles;
        Task.Run(() => ShellFileOperation.ChangeDateFromName(Data.CurrentADBDevice, items, Data.DirList.FileList, App.Current.Dispatcher));
    }

    public static void OpenEditor()
    {
        if (Data.FileActions.IsEditorOpen)
        {
            Data.FileActions.IsEditorOpen = false;
            return;
        }
        Data.FileActions.IsEditorOpen = true;

        Data.FileActions.EditorFilePath = Data.SelectedFiles.First().FullPath;

        var readTask = Task.Run(() =>
        {
            var text = "";
            try
            {
                text = ShellFileOperation.ReadAllText(Data.CurrentADBDevice, Data.FileActions.EditorFilePath);
            }
            catch (Exception)
            { }
            return text;
        });

        readTask.ContinueWith((t) => App.Current.Dispatcher.Invoke(() =>
        {
            Data.FileActions.EditorText =
            Data.FileActions.OriginalEditorText = t.Result;
        }));
    }

    public static void RestoreItems()
    {
        var restoreItems = (!Data.SelectedFiles.Any() ? Data.DirList.FileList : Data.SelectedFiles).Where(file => file.TrashIndex is not null && !string.IsNullOrEmpty(file.TrashIndex.OriginalPath));
        string[] existingItems = Array.Empty<string>();
        List<FileClass> existingFiles = new();
        bool merge = false;

        var restoreTask = Task.Run(() =>
        {
            existingItems = ADBService.FindFiles(Data.CurrentADBDevice.ID, restoreItems.Select(file => file.TrashIndex.OriginalPath));
            if (existingItems?.Any() is true)
            {
                if (restoreItems.Any(item => item.IsDirectory && existingItems.Contains(item.TrashIndex.OriginalPath)))
                    merge = true;

                existingItems = existingItems.Select(path => path[(path.LastIndexOf('/') + 1)..]).ToArray();
            }

            foreach (var item in restoreItems)
            {
                if (existingItems.Contains(item.FullName))
                    return;

                if (restoreItems.Count(file => file.FullName == item.FullName && file.TrashIndex.OriginalPath == item.TrashIndex.OriginalPath) > 1)
                {
                    existingItems = existingItems.Append(item.FullName).ToArray();
                    existingFiles.Add(item);
                    if (item.IsDirectory)
                        merge = true;
                }
            }
        });

        restoreTask.ContinueWith((t) =>
        {
            App.Current.Dispatcher.BeginInvoke(async () =>
            {
                if (existingItems.Length is int count and > 0)
                {
                    var result = await DialogService.ShowConfirmation(
                        Strings.S_CONFLICT_ITEMS(count),
                        Strings.S_RESTORE_CONF_TITLE,
                        primaryText: Strings.S_MERGE_REPLACE(merge),
                        secondaryText: count == restoreItems.Count() ? "" : "Skip",
                        cancelText: "Cancel",
                        icon: DialogService.DialogIcon.Exclamation);

                    if (result.Item1 is ContentDialogResult.None)
                    {
                        return;
                    }

                    if (result.Item1 is ContentDialogResult.Secondary)
                    {
                        restoreItems = existingFiles.Count != count
                            ? restoreItems.Where(item => !existingItems.Contains(item.FullName))
                            : restoreItems.Except(existingFiles);
                    }
                }

                ShellFileOperation.MoveItems(device: Data.CurrentADBDevice,
                                         items: restoreItems,
                                         targetPath: null,
                                         currentPath: Data.CurrentPath,
                                         fileList: Data.DirList.FileList,
                                         dispatcher: App.Current.Dispatcher);

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

    public static void CreateNewItem(FileClass file, string newName)
    {
        file.UpdatePath($"{Data.CurrentPath}{(Data.CurrentPath == "/" ? "" : "/")}{newName}");

        if (Data.Settings.ShowExtensions)
            file.UpdateType();

        try
        {
            if (file.Type is FileType.Folder)
                ShellFileOperation.MakeDir(Data.CurrentADBDevice, file.FullPath);
            else if (file.Type is FileType.File)
                ShellFileOperation.MakeFile(Data.CurrentADBDevice, file.FullPath);
            else
                throw new NotSupportedException();
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message, Strings.S_CREATE_ERR_TITLE, DialogService.DialogIcon.Critical);
            Data.DirList.FileList.Remove(file);
            throw;
        }

        file.IsTemp = false;
        file.ModifiedTime = DateTime.Now;
        if (file.Type is FileType.File)
            file.Size = 0;

        var index = Data.DirList.FileList.IndexOf(file);
        Data.DirList.FileList.Remove(file);
        Data.DirList.FileList.Insert(index, file);
        Data.FileActions.ItemToSelect = file;
    }

    public static bool IsPasteEnabled(bool ignoreSelected = false, bool isKeyboard = false)
    {
        // Explorer view but not trash or app drive
        if (Data.FileActions.IsPasteStateVisible)
        {
            Data.FileActions.CutItemsCount.Value = Data.CutItems.Count > 0 ? Data.CutItems.Count.ToString() : "";
            Data.FileActions.IsCutState.Value = Data.FileActions.PasteState is FileClass.CutType.Cut;
            Data.FileActions.IsCopyState.Value = Data.FileActions.PasteState is FileClass.CutType.Copy;
        }
        else
        {
            Data.FileActions.CutItemsCount.Value = "";
            Data.FileActions.IsCopyState.Value = false;
            Data.FileActions.IsCutState.Value = false;
        }

        Data.FileActions.PasteAction.Value = $"Paste {Data.CutItems.Count} {FileClass.CutTypeString(Data.FileActions.PasteState)} Item{(Data.CutItems.Count > 1 ? "s" : "")}";

        if (Data.CutItems.Count < 1 || Data.FileActions.IsRecycleBin || !Data.FileActions.IsExplorerVisible)
            return false;

        if (Data.CutItems.Count == 1 && Data.CutItems[0].Relation(Data.CurrentPath) is RelationType.Descendant or RelationType.Self)
            return false;

        var selected = ignoreSelected ? 0 : Data.SelectedFiles?.Count();
        switch (selected)
        {
            case 0:
                return !(Data.CutItems[0].ParentPath == Data.CurrentPath && Data.FileActions.PasteState is FileClass.CutType.Cut);
            case 1:
                if (isKeyboard && Data.FileActions.PasteState is FileClass.CutType.Copy && Data.CutItems[0].Relation(Data.SelectedFiles.First()) is RelationType.Self)
                    return true;

                var item = Data.SelectedFiles.First();
                if (!item.IsDirectory
                    || (Data.CutItems.Count == 1 && Data.CutItems[0].FullPath == item.FullPath)
                    || (Data.CutItems[0].ParentPath == item.FullPath))
                    return false;
                break;
            default:
                return false;
        }

        return true;
    }

    public static async void PasteFiles()
    {
        var firstSelectedFile = Data.SelectedFiles.Any() ? Data.SelectedFiles.First() : null;
        var targetName = "";
        var targetPath = "";

        if (Data.SelectedFiles.Count() != 1 || (Data.FileActions.PasteState is FileClass.CutType.Copy && Data.CutItems[0].Relation(firstSelectedFile) is RelationType.Self))
        {
            targetPath = Data.CurrentPath;
            targetName = Data.CurrentPath[Data.CurrentPath.LastIndexOf('/')..];
        }
        else
        {
            targetPath = Data.SelectedFiles.First().FullPath;
            targetName = FileHelper.DisplayName(Data.SelectedFiles.First());
        }

        var pasteItems = Data.CutItems.Where(f => f.Relation(targetPath) is not (RelationType.Self or RelationType.Descendant));
        await Task.Run(() => ShellFileOperation.MoveItems(Data.FileActions.PasteState is FileClass.CutType.Copy,
                                                          targetPath,
                                                          pasteItems,
                                                          targetName,
                                                          Data.DirList.FileList,
                                                          App.Current.Dispatcher,
                                                          Data.CurrentADBDevice,
                                                          Data.CurrentPath));

        if (Data.FileActions.PasteState is FileClass.CutType.Cut)
            FileHelper.ClearCutFiles(pasteItems);

        Data.FileActions.PasteEnabled = IsPasteEnabled();
    }

    public static void CutFiles(IEnumerable<FileClass> items, bool isCopy = false)
    {
        FileHelper.ClearCutFiles();
        Data.FileActions.PasteState = isCopy ? FileClass.CutType.Copy : FileClass.CutType.Cut;

        var itemsToCut = Data.DevicesObject.Current.Root is not AbstractDevice.RootStatus.Enabled
                    ? items.Where(file => file.Type is FileType.File or FileType.Folder) : items;

        foreach (var item in itemsToCut)
        {
            item.CutState = Data.FileActions.PasteState;
        }

        Data.CutItems.AddRange(itemsToCut);

        Data.FileActions.CopyEnabled = !isCopy;
        Data.FileActions.CutEnabled = isCopy;

        Data.FileActions.PasteEnabled = IsPasteEnabled();
        Data.FileActions.IsKeyboardPasteEnabled = IsPasteEnabled(true, true);
    }

    public static void Rename(TextBox textBox)
    {
        FileClass file = TextHelper.GetAltObject(textBox) as FileClass;
        var name = FileHelper.DisplayName(textBox);
        if (file.IsTemp)
        {
            if (string.IsNullOrEmpty(textBox.Text))
            {
                Data.DirList.FileList.Remove(file);
                return;
            }
            try
            {
                CreateNewItem(file, textBox.Text);
            }
            catch (Exception e)
            {
                if (e is NotImplementedException)
                    throw;
            }
        }
        else if (!string.IsNullOrEmpty(textBox.Text) && textBox.Text != name)
        {
            try
            {
                FileHelper.RenameFile(textBox.Text, file);
            }
            catch (Exception)
            { }
        }
    }

    public static async void DeleteFiles()
    {
        IEnumerable<FileClass> itemsToDelete;
        if (Data.FileActions.IsRecycleBin && !Data.SelectedFiles.Any())
        {
            itemsToDelete = Data.DirList.FileList.Where(f => !AdbExplorerConst.RECYCLE_INDEX_PATHS.Contains(f.FullPath));
        }
        else
        {
            itemsToDelete = Data.DevicesObject.Current.Root != AbstractDevice.RootStatus.Enabled
                    ? Data.SelectedFiles.Where(file => file.Type is FileType.File or FileType.Folder) : Data.SelectedFiles;
        }

        string deletedString;
        if (itemsToDelete.Count() == 1)
            deletedString = FileHelper.DisplayName(itemsToDelete.First());
        else
        {
            deletedString = $"{itemsToDelete.Count()} ";
            if (itemsToDelete.All(item => item.IsDirectory))
                deletedString += "folders";
            else if (itemsToDelete.All(item => !item.IsDirectory))
                deletedString += "files";
            else
                deletedString += "items";
        }

        var result = await DialogService.ShowConfirmation(
            Strings.S_DELETE_CONF(Data.FileActions.IsRecycleBin, deletedString),
            Strings.S_DEL_CONF_TITLE,
            Strings.S_DELETE_ACTION,
            checkBoxText: Data.Settings.EnableRecycle && !Data.FileActions.IsRecycleBin ? Strings.S_PERM_DEL : "",
            icon: DialogService.DialogIcon.Delete);

        if (result.Item1 is not ContentDialogResult.Primary)
            return;

        if (!Data.FileActions.IsRecycleBin && Data.Settings.EnableRecycle && !result.Item2)
        {
            ShellFileOperation.MoveItems(Data.CurrentADBDevice,
                                         itemsToDelete,
                                         AdbExplorerConst.RECYCLE_PATH,
                                         Data.CurrentPath,
                                         Data.DirList.FileList,
                                         App.Current.Dispatcher,
                                         Data.DevicesObject.Current);
        }
        else
        {
            ShellFileOperation.DeleteItems(Data.CurrentADBDevice, itemsToDelete, Data.DirList.FileList, App.Current.Dispatcher);

            if (Data.FileActions.IsRecycleBin)
            {
                TrashHelper.EnableRecycleButtons(Data.DirList.FileList.Except(itemsToDelete));
                if (!Data.SelectedFiles.Any() && Data.DirList.FileList.Any(item => AdbExplorerConst.RECYCLE_INDEX_PATHS.Contains(item.FullPath)))
                {
                    _ = Task.Run(() => ShellFileOperation.SilentDelete(Data.CurrentADBDevice, Data.DirList.FileList.Where(item => AdbExplorerConst.RECYCLE_INDEX_PATHS.Contains(item.FullPath))));
                }
            }
        }
    }

    public static void RefreshDrives(bool asyncClasify = false)
    {
        if (Data.DevicesObject.Current is null)
            return;

        if (!asyncClasify && Data.DevicesObject.Current.Drives?.Count > 0 && !Data.FileActions.IsExplorerVisible)
            asyncClasify = true;

        var driveTask = Task.Run(() =>
        {
            if (Data.CurrentADBDevice is null)
                return null;

            var drives = Data.CurrentADBDevice.GetDrives();

            if (Data.DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Trash))
                TrashHelper.UpdateRecycledItemsCount();

            if (Data.DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Temp))
                UpdateInstallersCount();

            if (Data.DevicesObject.Current.Drives.Any(d => d.Type is AbstractDrive.DriveType.Package))
                UpdatePackages();

            return drives;
        });
        driveTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || t.Result is null)
                return;

            App.Current.Dispatcher.Invoke(async () =>
            {
                if (await Data.DevicesObject.Current?.UpdateDrives(await t, App.Current.Dispatcher, asyncClasify))
                    Data.RuntimeSettings.FilterDrives = true;
            });
        });
    }

    public static void UpdateInstallersCount()
    {
        var countTask = Task.Run(() => ADBService.CountPackages(Data.DevicesObject.Current.ID));
        countTask.ContinueWith((t) => App.Current.Dispatcher.Invoke(() =>
        {
            if (!t.IsCanceled && Data.DevicesObject.Current is not null)
            {
                var temp = Data.DevicesObject.Current.Drives.Find(d => d.Type is AbstractDrive.DriveType.Temp);
                ((VirtualDriveViewModel)temp)?.SetItemsCount((long)t.Result);
            }
        }));
    }

    public static void UpdatePackages(bool updateExplorer = false)
    {
        Data.FileActions.ListingInProgress = true;

        var version = Data.DevicesObject.Current.AndroidVersion;
        var packageTask = Task.Run(() => ShellFileOperation.GetPackages(Data.CurrentADBDevice, Data.Settings.ShowSystemPackages, version is not null && version >= AdbExplorerConst.MIN_PKG_UID_ANDROID_VER));

        packageTask.ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            App.Current.Dispatcher.Invoke(() =>
            {
                Data.Packages = t.Result;
                if (updateExplorer)
                    Data.RuntimeSettings.ExplorerSource = Data.Packages;

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
        Data.DirList?.FileList?.Clear();
        Data.Packages.Clear();
        Data.FileActions.PushFilesFoldersEnabled =
        Data.FileActions.PullEnabled =
        Data.FileActions.DeleteEnabled =
        Data.FileActions.RenameEnabled =
        Data.FileActions.HomeEnabled =
        Data.FileActions.NewEnabled =
        Data.FileActions.PasteEnabled =
        Data.FileActions.UninstallVisible =
        Data.FileActions.CutEnabled =
        Data.FileActions.CopyEnabled =
        Data.FileActions.IsExplorerVisible =
        Data.FileActions.PackageActionsEnabled =
        Data.FileActions.IsCopyItemPathEnabled =
        Data.FileActions.UpdateModifiedEnabled =
        Data.FileActions.ParentEnabled = false;

        Data.FileActions.PushPackageEnabled = Data.Settings.EnableApk;

        Data.FileActions.ExplorerFilter = "";

        if (clearDevice)
        {
            Data.CurrentDisplayNames.Clear();
            Data.CurrentPath = null;
            Data.RuntimeSettings.CurrentBatteryContext = null;
            Data.FileActions.PushPackageEnabled = false;

            Data.RuntimeSettings.ClearNavBox = true;
        }

        Data.RuntimeSettings.FilterActions = true;
    }

    public static void UpdateFileActions()
    {
        Data.FileActions.UninstallPackageEnabled = Data.FileActions.IsAppDrive && Data.SelectedPackages.Any();
        Data.FileActions.ContextPushPackagesEnabled = Data.FileActions.IsAppDrive && !Data.SelectedPackages.Any();

        Data.FileActions.IsRefreshEnabled = Data.FileActions.IsDriveViewVisible || Data.FileActions.IsExplorerVisible;
        Data.FileActions.IsCopyCurrentPathEnabled = Data.FileActions.IsExplorerVisible && !Data.FileActions.IsRecycleBin && !Data.FileActions.IsAppDrive;

        if (Data.FileActions.IsAppDrive)
        {
            Data.FileActions.IsCopyItemPathEnabled = Data.SelectedPackages.Count() == 1;

            Data.RuntimeSettings.FilterActions = true;
            return;
        }

        Data.FileActions.IsRegularItem = !(Data.SelectedFiles.Any() && Data.DevicesObject.Current?.Root is not AbstractDevice.RootStatus.Enabled
            && Data.SelectedFiles.All(item => item is FileClass file && file.Type is not (FileType.File or FileType.Folder)));

        if (Data.FileActions.IsRecycleBin)
        {
            TrashHelper.EnableRecycleButtons(Data.SelectedFiles.Any() ? Data.SelectedFiles : Data.DirList.FileList);
        }
        else
        {
            Data.FileActions.DeleteEnabled = Data.SelectedFiles.Any() && Data.FileActions.IsRegularItem;
            Data.FileActions.RestoreEnabled = false;
        }

        Data.FileActions.DeleteAction.Value = Data.FileActions.IsRecycleBin && !Data.SelectedFiles.Any() ? Strings.S_EMPTY_TRASH : "Delete";
        Data.FileActions.RestoreAction.Value = Data.FileActions.IsRecycleBin && !Data.SelectedFiles.Any() ? Strings.S_RESTORE_ALL : "Restore";

        Data.FileActions.PullEnabled = Data.FileActions.PushPullEnabled && !Data.FileActions.IsRecycleBin && Data.SelectedFiles.Any() && Data.FileActions.IsRegularItem;
        Data.FileActions.ContextPushEnabled = Data.FileActions.PushPullEnabled && !Data.FileActions.IsRecycleBin && (!Data.SelectedFiles.Any() || (Data.SelectedFiles.Count() == 1 && Data.SelectedFiles.First().IsDirectory));

        Data.FileActions.RenameEnabled = !Data.FileActions.IsRecycleBin && Data.SelectedFiles.Count() == 1 && Data.FileActions.IsRegularItem;

        Data.FileActions.CutEnabled = !Data.SelectedFiles.All(file => file.CutState is FileClass.CutType.Cut) && Data.FileActions.IsRegularItem;

        Data.FileActions.CopyEnabled = !Data.FileActions.IsRecycleBin && Data.FileActions.IsRegularItem && !Data.SelectedFiles.All(file => file.CutState is FileClass.CutType.Copy);
        Data.FileActions.PasteEnabled = IsPasteEnabled();
        Data.FileActions.IsKeyboardPasteEnabled = IsPasteEnabled(true, true);

        Data.FileActions.PackageActionsEnabled = Data.Settings.EnableApk && Data.SelectedFiles.Any() && Data.SelectedFiles.All(file => file.IsInstallApk) && !Data.FileActions.IsRecycleBin;
        Data.FileActions.IsCopyItemPathEnabled = Data.SelectedFiles.Count() == 1 && !Data.FileActions.IsRecycleBin;

        Data.FileActions.ContextNewEnabled = !Data.SelectedFiles.Any() && !Data.FileActions.IsRecycleBin;
        Data.FileActions.SubmenuUninstallEnabled = Data.FileActions.IsTemp && Data.SelectedFiles.Any() && Data.SelectedFiles.All(file => file.IsInstallApk);

        Data.FileActions.UpdateModifiedEnabled = !Data.FileActions.IsRecycleBin && Data.SelectedFiles.Any() && Data.SelectedFiles.All(file => file.Type is FileType.File && !file.IsApk);

        Data.FileActions.EditFileEnabled = !Data.FileActions.IsRecycleBin
            && Data.SelectedFiles.Count() == 1
            && Data.SelectedFiles.First().Type is FileType.File
            && !Data.SelectedFiles.First().IsApk;

        Data.RuntimeSettings.FilterActions = true;
    }

    public static void PushItems(bool isFolderPicker, bool isContextMenu)
    {
        Data.RuntimeSettings.IsPathBoxFocused = false;

        FilePath targetPath;
        if (isContextMenu && Data.SelectedFiles.Count() == 1)
            targetPath = Data.SelectedFiles.First();
        else
            targetPath = new(Data.CurrentPath);

        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = isFolderPicker,
            Multiselect = true,
            DefaultDirectory = Data.Settings.DefaultFolder,
            Title = Strings.S_PUSH_BROWSE_TITLE(isFolderPicker, targetPath.FullPath == Data.CurrentPath ? "" : targetPath.FullName),
        };

        if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
            return;

        foreach (var item in dialog.FilesAsShellObject)
        {
            var pushOpeartion = new FilePushOperation(App.Current.Dispatcher, Data.CurrentADBDevice, new FilePath(item), targetPath);
            pushOpeartion.PropertyChanged += PushOpeartion_PropertyChanged;
            Data.FileOpQ.AddOperation(pushOpeartion);
        }
    }

    private static void PushOpeartion_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var pushOperation = sender as FilePushOperation;

        // If operation completed now and current path is where the new file was pushed to and it is not shown yet
        if ((e.PropertyName == "Status") &&
            (pushOperation.Status == FileOperation.OperationStatus.Completed) &&
            (pushOperation.TargetPath.FullPath == Data.CurrentPath) &&
            (!Data.DirList.FileList.Any(f => f.FullName == pushOperation.FilePath.FullName)))
        {
            Data.DirList.FileList.Add(FileClass.FromWindowsPath(pushOperation.TargetPath, pushOperation.FilePath));
        }
    }

    public static void PullFiles(bool quick = false)
    {
        Data.RuntimeSettings.IsPathBoxFocused = false;

        int itemsCount = Data.SelectedFiles.Count();
        ShellObject path;

        if (quick)
        {
            path = ShellObject.FromParsingName(Data.Settings.DefaultFolder);
        }
        else
        {
            var dialog = new CommonOpenFileDialog()
            {
                IsFolderPicker = true,
                Multiselect = false,
                DefaultDirectory = Data.Settings.DefaultFolder,
                Title = Strings.S_ITEMS_DESTINATION(itemsCount > 1, Data.SelectedFiles.First()),
            };

            if (dialog.ShowDialog() != CommonFileDialogResult.Ok)
                return;

            path = dialog.FileAsShellObject;
        }

        var dirPath = new FilePath(path);

        if (!Directory.Exists(path.ParsingName))
        {
            try
            {
                Directory.CreateDirectory(path.ParsingName);
            }
            catch (Exception e)
            {
                DialogService.ShowMessage(e.Message, Strings.S_DEST_ERR, DialogService.DialogIcon.Critical);
                return;
            }
        }

        foreach (FileClass item in Data.SelectedFiles)
        {
            Data.FileOpQ.AddOperation(new FilePullOperation(App.Current.Dispatcher, Data.CurrentADBDevice, item, dirPath));
        }
    }
}
