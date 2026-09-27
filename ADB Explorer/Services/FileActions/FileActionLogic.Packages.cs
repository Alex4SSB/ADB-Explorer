using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
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

        var targetPath = PickDestinationFolder(packages.Count, packages[0].Name);
        if (targetPath is null)
            return;

        ShellFileOperation.BackupPackages(ActionDevice, packages, targetPath, App.AppDispatcher);
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
        Data.CopyPaste.MarkSelfClipboardWritten();
    }

    public static void UpdateInstallersCount(CancellationToken cancellationToken = default)
    {
        var countTask = Task.Run(() => AdbService.CountPackages(Data.ActiveDevice.ID), cancellationToken);
        countTask.ContinueWith((t) => App.SafeInvoke(() =>
        {
            if (!t.IsCanceled && Data.ActiveDevice is not null)
            {
                var temp = Data.ActiveDevice.Drives.Find(d => d.Type is AbstractDrive.DriveType.Temp);
                ((VirtualDriveViewModel)temp)?.SetItemsCount((long)t.Result);
            }
        }), cancellationToken);
    }

    public static void UpdatePackagesCount(CancellationToken cancellationToken = default)
    {
        var packageTask = Task.Run(() => ShellFileOperation.GetPackagesCount(Data.ActiveDevice, Data.Settings.ShowSystemPackages), cancellationToken);

        packageTask.ContinueWith((t) =>
        {
            if (t.IsCanceled || t.Result is null || Data.ActiveDevice is null)
                return;

            App.SafeInvoke(() =>
            {
                var package = Data.ActiveDevice.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
                ((VirtualDriveViewModel)package)?.SetItemsCount((int?)t.Result);
            });
        });
    }

    public static void UpdatePackages(bool updateExplorer = false, CancellationToken cancellationToken = default, bool cacheOnly = false, ExplorerInstance? instance = null)
    {
        // Defaults to the active tab (fine for Refresh()); a caller navigating a specific tab
        // should pass it explicitly, since it may no longer be active once this async listing finishes.
        instance ??= Data.ActiveExplorerInstance;

        Data.FileActions.ListingInProgress = true;

        var version = Data.ActiveDevice.AndroidVersion;
        var packageTask = Task.Run(() => ShellFileOperation.GetPackages(Data.ActiveDevice, Data.Settings.ShowSystemPackages, version is not null && version >= AdbExplorerConst.MIN_PKG_UID_ANDROID_VER), cancellationToken);

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

                if (updateExplorer && !ReferenceEquals(instance.ExplorerSource, Data.Packages))
                    instance.ExplorerSource = Data.Packages;

                if (!updateExplorer && Data.ActiveDevice is not null)
                {
                    var package = Data.ActiveDevice.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
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

    public static void PullPackages(string targetPath = "")
    {
        var packages = Data.SelectedPackages.ToList();
        if (packages.Count == 0)
            return;

        if (string.IsNullOrEmpty(targetPath))
        {
            var picked = PickDestinationFolder(packages.Count, packages[0].Name);
            if (picked is null)
                return;

            targetPath = picked;
        }

        var pullItems = FileHelper.GetFilesFromTree(
            FileHelper.GetFolderTree(packages.Select(p => p.Path), false, Data.DeviceCts.Token));

        PullFiles(targetPath, pullItems, true);
    }

    public static void OpenApkLocation(Package? apk = null)
    {
        apk ??= Data.SelectedPackages.First();

        Data.RequestNavigation(new(FileHelper.GetParentPath(apk.Path)));
    }

    public static void ApkWebSearch()
    {
        var apk = Data.SelectedPackages.First();
        
        Network.OpenBrowserSearch(apk.Name, Data.RuntimeSettings.DefaultBrowserPath);
    }
}
