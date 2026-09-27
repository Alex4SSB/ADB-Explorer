namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    /// <param name="device">Defaults to the active tab's device, which can differ from the
    /// app-wide <see cref="Devices.Current"/> when tabs browse different devices.</param>
    public static void RefreshDrives(bool asyncClassify, CancellationToken cancellationToken, LogicalDeviceViewModel? device = null)
    {
        device ??= Data.ActiveExplorerInstance.EffectiveDevice;
        if (device is null)
            return;

        if (!asyncClassify && device.Drives?.Count > 0 && !Data.FileActions.IsExplorerVisible)
            asyncClassify = true;

        var driveTask = Task.Run(() =>
        {
            bool countRecycle = false, countPackages = false, countInstallers = false;
            if (device.Type is not DeviceType.Recovery)
            {
                countRecycle = Data.Settings.EnableRecycle && device.Drives.Any(d => d.Type is AbstractDrive.DriveType.Trash);
                countInstallers = Data.Settings.EnableApk && device.Drives.Any(d => d.Type is AbstractDrive.DriveType.Temp);
                countPackages = Data.Settings.EnableApk && device.Drives.Any(d => d.Type is AbstractDrive.DriveType.Package);
            }

            return AdbService.GetDrives(
                device.ID,
                device.Type,
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
                if (device.Type is DeviceType.Recovery)
                {
                    foreach (var item in device.Drives.OfType<VirtualDriveViewModel>())
                        item.SetItemsCount(item.Type is AbstractDrive.DriveType.Package ? -1 : null);
                }
                else
                {
                    ApplyVirtualDriveCounts(device, result);
                }

                if (App.AppDispatcher is null)
                    return;

                if (await device.UpdateDrives(result.Drives, App.AppDispatcher, asyncClassify))
                {
                    Data.RequestExplorer(ExplorerRequest.FilterDrives);
                    FolderHelper.CombineDisplayNames(device);
                }
            });
        }, cancellationToken);
    }

    private static void ApplyVirtualDriveCounts(LogicalDeviceViewModel device, DrivePollResult result)
    {
        if (result.RecycleCount is long recycleCount)
        {
            var trash = device.Drives.Find(d => d.Type is AbstractDrive.DriveType.Trash);
            ((VirtualDriveViewModel)trash)?.SetItemsCount(recycleCount);
        }

        if (result.InstallersCount is ulong installersCount)
        {
            var temp = device.Drives.Find(d => d.Type is AbstractDrive.DriveType.Temp);
            ((VirtualDriveViewModel)temp)?.SetItemsCount((long)installersCount);
        }

        if (result.PackagesCount is ulong packagesCount)
        {
            var package = device.Drives.Find(d => d.Type is AbstractDrive.DriveType.Package);
            ((VirtualDriveViewModel)package)?.SetItemsCount((int)packagesCount);
        }
    }
}
