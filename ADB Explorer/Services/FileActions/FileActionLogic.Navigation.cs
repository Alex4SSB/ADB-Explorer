using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    public static void Refresh()
    {
        if (Data.FileActions.IsAppDrive)
        {
            UpdatePackages(updateExplorer: true, cacheOnly: true);
            return;
        }

        if (Data.FileActions.IsDriveViewVisible)
        {
            Data.RequestNavigation(new(Navigation.SpecialLocation.DriveView));
            return;
        }

        Data.RequestNavigation(new(Data.CurrentPath));
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

    public static async void FollowLink()
    {
        var target = Data.SelectedFiles.First().LinkTarget;

        if (string.IsNullOrEmpty(target))
            return;

        if (FileHelper.GetParentPath(target) != Data.CurrentPath)
        {
            Data.RequestNavigation(new(target + "/.."));
        }

        await AsyncHelper.WaitUntil(() => !Data.DirList.InProgress, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(20), new());

        var file = Data.DirList.FileList.FirstOrDefault(f => f.FullPath == target);
        if (file is not null)
            Data.ItemToSelect.Value = file;
    }

    public static async void OpenItemLocation()
    {
        var file = Data.SelectedFiles.FirstOrDefault();
        if (file is null)
            return;

        var target = file.FullPath;
        var parentPath = FileHelper.GetParentPath(target);

        if (parentPath != Data.CurrentPath)
        {
            Data.RequestNavigation(new(parentPath));
        }

        await AsyncHelper.WaitUntil(() => !Data.DirList.InProgress, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(20), new());

        var found = Data.DirList.FileList.FirstOrDefault(f => f.FullPath == target);
        if (found is not null)
            Data.ItemToSelect.Value = found;
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

        if (device is not null && Data.ActiveDevice?.ID != device.ID)
        {
            Data.RuntimeSettings.PendingLocationAfterDeviceOpen = new(path);
            DeviceHelper.BrowseDeviceAction(device);
            return;
        }

        Data.RequestNavigation(new(path));
    }

    /// <summary>
    /// Folder, archive or drive location that opens in a new tab, or null when the item can't. In search mode
    /// any other item opens its containing folder, as its results come from all over.
    /// </summary>
    internal static string? GetNewTabPath(object? item, LogicalDeviceViewModel device, bool isAppDrive, bool isArchive, bool isSearchMode = false)
    {
        return item switch
        {
            DriveViewModel drive => drive.Path,
            FileClass { Type: FileType.Folder } folder => string.IsNullOrEmpty(folder.LinkTarget) ? folder.FullPath : folder.LinkTarget,
            FileClass archive when !isAppDrive
                && ArchiveHelper.CanNavigateIntoArchive(archive.FullPath, archive.FullName, device.ID, isArchive) => ArchivePath.Join(archive.FullPath, ""),
            FileClass file when isSearchMode && !isAppDrive => FileHelper.GetParentPath(file.FullPath),
            _ => null,
        };
    }

    private static object? GetNewTabItem(FileList list)
    {
        if (ReferenceEquals(list, Data.Files) && list.Actions.IsDriveViewVisible)
            return Data.RuntimeSettings.SelectedDrive;

        if (list.SelectedFiles.Count() == 1)
            return list.SelectedFiles.First();

        return null;
    }

    public static void OpenInNewTab()
    {
        if (!ReferenceEquals(Data.Active, Data.Files) && ExplorerTree?.ContextTarget is { } node)
        {
            ExplorerTree.OpenNodeInNewTab(node);
            return;
        }

        if (ActionDevice is not { } device)
            return;

        var path = GetNewTabPath(GetNewTabItem(ActionList), device, ActionFlags.IsAppDrive, ActionFlags.IsArchive, ActionFlags.IsSearchMode);
        if (!string.IsNullOrEmpty(path))
            DeviceHelper.OpenDeviceInNewTab(device, new AdbLocation(path));
    }

    internal static bool CanEnterSelection(FileClass file)
        => file.IsDirectory
        || (ActionDevice is { } device
            && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, device.ID, ActionFlags.IsArchive));
}
