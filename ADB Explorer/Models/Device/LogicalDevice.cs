using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Models;

/// <summary>
/// Represents all devices acquired by <code>adb devices</code>
/// </summary>
public class LogicalDevice : Device
{
    #region Full properties

    private string name;
    public string Name
    {
        get => name;
        set => Set(ref name, value);
    }

    private RootStatus root = RootStatus.Unchecked;
    public RootStatus Root
    {
        get => root;
        set => Set(ref root, value);
    }

    private Battery battery;
    public Battery Battery
    {
        get => battery;
        protected set => Set(ref battery, value);
    }

    private ObservableList<DriveViewModel> drives = new();
    public ObservableList<DriveViewModel> Drives
    {
        get => drives;
        set => Set(ref drives, value);
    }

    #endregion

    private LogicalDevice(string name, string id)
    {
        Name = name;
        ID = id;

        Battery = new Battery();

        InitDeviceDrives();
    }

    public static LogicalDevice New(string name, string id, string status)
    {
        var deviceType = DeviceHelper.GetType(id, status);
        var deviceStatus = DeviceHelper.GetStatus(status);
        var ip = deviceType is DeviceType.Remote ? id.Split(':')[0] : "";
        var rootStatus = deviceType is DeviceType.Recovery
            ? RootStatus.Enabled
            : RootStatus.Unchecked;

        if (deviceType is DeviceType.WSA && name.ToLower().Contains("subsystem"))
            name = "WSA";

        return new LogicalDevice(name, id) { Type = deviceType, Status = deviceStatus, Root = rootStatus, IpAddress = ip };
    }

    public void EnableRoot(bool enable)
    {
        Root = enable
            ? ADBService.Root(this) ? RootStatus.Enabled : RootStatus.Forbidden
            : ADBService.Unroot(this) ? RootStatus.Disabled : RootStatus.Unchecked;
    }

    public void UpdateBattery()
    {
        Battery.Update(ADBService.AdbDevice.GetBatteryInfo(this));
    }

    #region Drive handling

    private void InitDeviceDrives()
    {
        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Root).Key)));
        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Internal).Key)));

        Drives.Add(new VirtualDriveViewModel(new(path: NavHistory.StringFromLocation(NavHistory.SpecialLocation.RecycleBin), -1)));
        Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.TEMP_PATH)));
        Drives.Add(new VirtualDriveViewModel(new(path: NavHistory.StringFromLocation(NavHistory.SpecialLocation.PackageDrive))));
    }

    /// <summary>
    /// Update <see cref="Device"/> with new drives
    /// </summary>
    /// <param name="drives">The new drives to be assigned</param>
    /// <param name="asyncClassify"><see langword="true"/> to update only after fully acquiring all information</param>
    public async Task<bool> UpdateDrives(IEnumerable<Drive> drives, Dispatcher dispatcher, bool asyncClassify = false)
    {
        bool collectionChanged;

        // MMC and OTG drives are searched for and only then UI is updated with all changes
        if (asyncClassify)
        {
            collectionChanged = await UpdateExtensionDrivesAsync(drives, dispatcher);
        }
        // All drives are first updated in UI, and only then MMC and OTG drives are searched for
        else
        {
            collectionChanged = SetDrives(drives);
            UpdateExtensionDrives(drives, dispatcher);
        }

        return collectionChanged;
    }

    private void UpdateExtensionDrives(IEnumerable<Drive> drives, Dispatcher dispatcher)
    {
        var mmcTask = Task.Run(() => DeviceHelper.GetMmcDrive(drives.OfType<LogicalDrive>(), ID));
        mmcTask.ContinueWith((t) =>
        {
            if (t.IsCanceled)
                return;

            dispatcher.BeginInvoke(() =>
            {
                SetMmcDrive(t.Result);
                SetExternalDrives();
            });
        });
    }

    private async Task<bool> UpdateExtensionDrivesAsync(IEnumerable<Drive> drives, Dispatcher dispatcher)
    {
        await Task.Run(() =>
        {
            if (DeviceHelper.GetMmcDrive(drives.OfType<LogicalDrive>(), ID) is LogicalDrive mmc)
                mmc.Type = AbstractDrive.DriveType.Expansion;

            DeviceHelper.SetExternalDrives(drives.OfType<LogicalDrive>());
        });

        var result = false;
        await dispatcher.BeginInvoke(() => result = SetDrives(drives));

        return result;
    }

    /// <summary>
    /// Update drive parameters, add new drives, remove non-existent drives
    /// </summary>
    /// <param name="drives"></param>
    /// <returns><see langword="true"/> if drives have been added or removed</returns>
    private bool SetDrives(IEnumerable<Drive> drives)
    {
        if (drives is null)
            return false;

        bool added = false;

        foreach (var other in drives)
        {
            // Accommodate for changing the path to /sdcard
            var selfQ = Drives.Where(d => d.Path == other.Path || (other.Type is AbstractDrive.DriveType.Internal && d.Type is AbstractDrive.DriveType.Internal));
            if (selfQ.Any())
            {
                // Update the drive if it exists
                var self = selfQ.First();

                switch (self)
                {
                    case LogicalDriveViewModel logical:
                        logical.SetParams((LogicalDrive)other);
                        if (other.Type is not AbstractDrive.DriveType.Unknown)
                            logical.SetType(other.Type);
                        break;
                    case VirtualDriveViewModel virt:
                        virt.SetItemsCount(((VirtualDrive)other).ItemsCount);
                        break;
                    default:
                        throw new NotSupportedException();
                }
            }
            // Create a new drive if it doesn't exist
            else if (other is LogicalDrive logical)
            {
                Drives.Add(new LogicalDriveViewModel(logical));
                added = true;
            }
            else if (other is VirtualDrive virt && !Drives.Any(d => d.Type == virt.Type))
            {
                Drives.Add(new VirtualDriveViewModel(virt));
                added = true;
            }
            else
                throw new NotSupportedException();
        }

        // Remove all drives that were not discovered in the last update
        var removed = Drives.RemoveAll(self => self is LogicalDriveViewModel
                                               && !drives.Any(other => other.Path == self.Path
                                                    || (other.Type is AbstractDrive.DriveType.Internal && self.Type is AbstractDrive.DriveType.Internal)));

        return added || removed;
    }

    public void SetMmcDrive(LogicalDrive mmcDrive)
    {
        if (mmcDrive is null)
            return;

        ((LogicalDriveViewModel)Drives.FirstOrDefault(d => d.Path == mmcDrive.Path))?.SetExtension();
    }

    /// <summary>
    /// Sets type of all <see cref="DriveViewModel"/> with unknown type as external. Changes the local property.
    /// </summary>
    public void SetExternalDrives()
    {
        if (drives is null)
            return;

        foreach (var item in Drives.Where(d => d.Type == AbstractDrive.DriveType.Unknown))
        {
            ((LogicalDriveViewModel)item).SetExtension(false);
        }
    }

    #endregion

    public override string ToString() => Name;
}
