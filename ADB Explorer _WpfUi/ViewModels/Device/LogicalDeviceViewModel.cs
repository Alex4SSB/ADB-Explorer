using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using AdvancedSharpAdbClient.Models;

namespace ADB_Explorer.ViewModels;

public partial class LogicalDeviceViewModel : DeviceViewModel
{
    #region Full properties

    [ObservableProperty]
    public new partial LogicalDevice Device { get; set; }

    private bool isOpen;
    /// <summary>
    /// Device is open for browsing
    /// </summary>
    public bool IsOpen
    {
        get => isOpen;
        private set
        {
            if (Set(ref isOpen, value) && Root is RootStatus.Enabled)
            {
                if (!value && Data.Settings.UnrootOnDisconnect is true)
                    ADBService.Unroot(Device.ID);

                if (value)
                    Data.RuntimeSettings.IsRootActive = true;
            }
        }
    }

    public DeviceData DeviceData => Device.DeviceData;

    public byte? AndroidVersion => byte.TryParse(AndroidVersionString?.Split('.')[0], out byte ver) ? ver : null;

    [ObservableProperty]
    public partial bool UseIdForName { get; set; }

    [ObservableProperty]
    public partial ObservableList<DriveViewModel> Drives { get; set; } = [];

    #endregion

    #region Read only properties

    public string Name
    {
        get
        {
            // to prevent displaying [Service] for offline connect services acquired via adb devices -l upon first contact
            if (string.IsNullOrEmpty(Device.Name) && DiscoverTime - DateTime.Now < AdbExplorerConst.SERVICE_DISPLAY_DELAY)
                return " ";

            return UseIdForName ? Device.ID : Device.Name;
        }
    }

    public string BaseID => Type is DeviceType.Service ? ID.Split('.')[0] : ID;

    public string LogicalID => Type is DeviceType.Service && ID.Count(c => c is '-') > 1 ? ID.Split('-')[1] : ID;

    public RootStatus Root => Device.Root;

    public string RootString => Root switch
    {
        RootStatus.Unchecked => Strings.Resources.S_STAT_ROOT_UNCHECKED,
        RootStatus.Forbidden => Strings.Resources.S_STAT_ROOT_FORBIDDEN,
        RootStatus.Disabled => Strings.Resources.S_DISABLED,
        RootStatus.Enabled => Strings.Resources.S_ENABLED,
        _ => throw new NotSupportedException(),
    };

    public bool AndroidVersionIncompatible => AndroidVersion is not null && AndroidVersion < AdbExplorerConst.MIN_SUPPORTED_ANDROID_VER;

    public override string Tooltip
    {
        get
        {
            var type = Device.Type switch
            {
                DeviceType.Local => Strings.Resources.S_TYPE_USB,
                DeviceType.Remote => Strings.Resources.S_TYPE_WIFI,
                DeviceType.Emulator => Strings.Resources.S_TYPE_EMULATOR,
                DeviceType.WSA => Strings.Resources.S_TYPE_WSA,
                DeviceType.Service => Strings.Resources.S_TYPE_SERVICE,
                DeviceType.Recovery => $"{Strings.Resources.S_TYPE_USB} ({Strings.Resources.S_RECOVERY_MODE})",
                DeviceType.Sideload => $"{Strings.Resources.S_TYPE_USB} ({Strings.Resources.S_REBOOT_SIDELOAD})",
                _ => throw new NotSupportedException(),
            };

            var status = Device.Status switch
            {
                DeviceStatus.Ok => "{0}",
                DeviceStatus.Offline => Strings.Resources.S_STATUS_OFFLINE,
                DeviceStatus.Unauthorized => Strings.Resources.S_STATUS_UNAUTH,
                _ => throw new NotSupportedException(),
            };

            return string.Format(status, type);
        }
    }

    public Battery Battery => Device.Battery;

    #endregion

    #region Device properties (lazy-loaded from ADB)

    public Dictionary<string, string> Props
    {
        get
        {
            if (field is null)
            {
                int exitCode = ADBService.ExecuteDeviceAdbShellCommand(ID, ADBService.GET_PROP, out string stdout, out string stderr, CancellationToken.None);
                if (exitCode == 0)
                {
                    field = stdout.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Where(
                        l => l[0] == '[' && l[^1] == ']').TryToDictionary(
                            line => line.Split(':')[0].Trim('[', ']', ' '),
                            line => line.Split(':')[1].Trim('[', ']', ' '));
                }
                else
                    field = [];
            }

            return field;
        }
    }

    public string? BrandName
    {
        get
        {
            if (string.IsNullOrEmpty(field))
            {
                field = Props.GetValueOrDefault(ADBService.BRAND_NAME);
                if (field is not null)
                    Device.Name = field;
            }
            return field;
        }
    } = null;

    public string? MmcProp
    {
        get
        {
            if (string.IsNullOrEmpty(field))
            {
                field = Props.GetValueOrDefault(ADBService.MMC_PROP);
            }
            return field;
        }
    } = null;

    public string? OtgProp
    {
        get
        {
            if (string.IsNullOrEmpty(field))
            {
                field = Props.GetValueOrDefault(ADBService.OTG_PROP);
            }
            return field;
        }
    } = null;

    public string? AndroidVersionString
    {
        get
        {
            if (string.IsNullOrEmpty(field))
            {
                field = Props.GetValueOrDefault(ADBService.ANDROID_VERSION, "");
            }
            return field;
        }
    } = "";

    public Task<string?> GetAndroidVersion() => Task.Run(() => AndroidVersionString);

    #endregion

    public static implicit operator string(LogicalDeviceViewModel vm) => vm?.ID;

    public DateTime DiscoverTime { get; }

    #region Commands

    public DeviceAction BrowseCommand { get; }
    public DeviceAction RemoveCommand { get; }
    public DeviceAction ToggleRootCommand { get; }
    public DeviceAction SideloadCommand { get; }
    public List<object> RebootCommands { get; } = [];

    #endregion

    public LogicalDeviceViewModel(LogicalDevice device) : base(device)
    {
        DiscoverTime = DateTime.Now;

        Device = device;
        if (Device.Type is DeviceType.Emulator)
            UseIdForName = true;

        InitDeviceDrives();

        BrowseCommand = new(() => !IsOpen && device.Status is DeviceStatus.Ok && device.Type is not DeviceType.Sideload,
                            () => DeviceHelper.BrowseDeviceAction(this));

        RemoveCommand = DeviceHelper.RemoveDeviceCommand(this);

        ToggleRootCommand = DeviceHelper.ToggleRootDeviceCommand(this);

        App.SafeInvoke(() =>
        {
            Thread.CurrentThread.CurrentCulture =
            Thread.CurrentThread.CurrentUICulture = Data.Settings.UICulture;

            foreach (RebootCommand.RebootType item in Enum.GetValues<RebootCommand.RebootType>())
            {
                RebootCommands.Add(new RebootCommand(this, item));

                if (item is RebootCommand.RebootType.Title)
                    RebootCommands.Add(new Separator() { Margin = new(-11, 0, -11, 0) });
            }
        });

        SideloadCommand = new(() => Device.Type is DeviceType.Sideload or DeviceType.Recovery && device.Status is DeviceStatus.Ok,
                              () => DeviceHelper.SideloadDeviceAction(this));

        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.DeviceToOpen))
        {
            IsOpen = Data.RuntimeSettings.DeviceToOpen is not null
                && Data.RuntimeSettings.DeviceToOpen.ID == ID;
        }
    }

    #region Setter functions

    public void SetAndroidVersion()
    {
        if (!IsOpen)
            return;

        OnPropertyChanged(nameof(AndroidVersion));
    }

    public void UpdateDevice(LogicalDeviceViewModel other) => UpdateDevice(other.Device);

    public void UpdateDevice(LogicalDevice other)
    {
        Device.Name = other.Name;
        SetStatus(other.Status);
    }

    public void EnableRoot(bool enable)
    {
        Device.Root = enable
            ? ADBService.Root(Device.ID) ? RootStatus.Enabled : RootStatus.Forbidden
            : ADBService.Unroot(Device.ID) ? RootStatus.Disabled : RootStatus.Unchecked;

        if (Data.DevicesObject.Current.ID == ID)
            Data.RuntimeSettings.IsRootActive = Root is RootStatus.Enabled;

        OnPropertyChanged(nameof(Root));
    }

    public bool SetRootStatus(RootStatus status)
    {
        if (Root != status)
        {
            Device.Root = status;
            OnPropertyChanged(nameof(Root));
            OnPropertyChanged(nameof(RootString));

            if (IsOpen)
                Data.RuntimeSettings.IsRootActive = status is RootStatus.Enabled;

            return true;
        }

        return false;
    }

    public void UpdateBattery()
    {
        Device.Battery.Update(ADBService.GetBatteryInfo(this));
    }

    public void UpdateName() => OnPropertyChanged(nameof(Name));

    #endregion

    #region Drive handling

    private void InitDeviceDrives()
    {
        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Root).Key)));
        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Internal).Key)));

        Drives.Add(new VirtualDriveViewModel(new(path: AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin), -1)));
        Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.TEMP_PATH)));
        Drives.Add(new VirtualDriveViewModel(new(path: AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive))));
    }

    public async Task<bool> UpdateDrives(IEnumerable<DriveSnapshot> snapshots, Dispatcher dispatcher, bool asyncClassify = false)
    {
        bool collectionChanged;

        if (asyncClassify)
        {
            collectionChanged = await UpdateExtensionDrivesAsync(snapshots, dispatcher);
        }
        else
        {
            collectionChanged = SetDrives(snapshots);
            UpdateExtensionDrives(snapshots, dispatcher);
        }

        return collectionChanged;
    }

    private void UpdateExtensionDrives(IEnumerable<DriveSnapshot> snapshots, Dispatcher dispatcher)
    {
        var mmcTask = Task.Run(() => DeviceHelper.GetMmcDrive(snapshots, ID));
        mmcTask.ContinueWith(t =>
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

    private async Task<bool> UpdateExtensionDrivesAsync(IEnumerable<DriveSnapshot> snapshots, Dispatcher dispatcher)
    {
        var list = snapshots.ToList();

        await Task.Run(() =>
        {
            if (DeviceHelper.GetMmcDrive(list, ID) is { } mmc)
            {
                var idx = list.IndexOf(mmc);
                list[idx] = mmc with { Type = AbstractDrive.DriveType.Expansion };
            }

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Type is AbstractDrive.DriveType.Unknown)
                    list[i] = list[i] with { Type = AbstractDrive.DriveType.External };
            }
        });

        var result = false;
        await dispatcher.BeginInvoke(() => result = SetDrives(list));
        return result;
    }

    private bool SetDrives(IEnumerable<DriveSnapshot> snapshots)
    {
        if (snapshots is null)
            return false;

        bool added = false;

        foreach (var snapshot in snapshots)
        {
            var selfQ = Drives.Where(d => d.Path == snapshot.Path
                || (snapshot.Type is AbstractDrive.DriveType.Internal && d.Type is AbstractDrive.DriveType.Internal));

            if (selfQ.Any())
            {
                var self = selfQ.First() as LogicalDriveViewModel
                    ?? throw new NotSupportedException();

                self.UpdateDrive(snapshot);
                if (snapshot.Type is not AbstractDrive.DriveType.Unknown)
                    self.SetType(snapshot.Type);
            }
            else
            {
                Drives.Add(new LogicalDriveViewModel(LogicalDrive.From(snapshot)));
                added = true;
            }
        }

        var removed = Drives.RemoveAll(self => self is LogicalDriveViewModel
            && !snapshots.Any(s => s.Path == self.Path
                || (s.Type is AbstractDrive.DriveType.Internal && self.Type is AbstractDrive.DriveType.Internal)));

        return added || removed;
    }

    public void SetMmcDrive(DriveSnapshot? mmc)
    {
        if (mmc is not { } mmcDrive)
            return;

        ((LogicalDriveViewModel)Drives.FirstOrDefault(d => d.Path == mmcDrive.Path))?.SetExtension();
    }

    /// <summary>
    /// Update drive parameters, add new drives, remove non-existent drives
    /// </summary>
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
                        logical.UpdateDrive((LogicalDrive)other);
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
    /// Sets type of all <see cref="DriveViewModel"/> with unknown type as external.
    /// </summary>
    public void SetExternalDrives()
    {
        foreach (var item in Drives.Where(d => d.Type == AbstractDrive.DriveType.Unknown))
        {
            ((LogicalDriveViewModel)item).SetExtension(false);
        }
    }

    #endregion
}

public class LogicalDeviceViewModelEqualityComparer : IEqualityComparer<LogicalDeviceViewModel>
{
    public bool Equals(LogicalDeviceViewModel x, LogicalDeviceViewModel y)
    {
        return x.ID == y.ID && x.Status == y.Status && x.DeviceData == y.DeviceData;
    }

    public int GetHashCode([DisallowNull] LogicalDeviceViewModel obj)
    {
        return (obj.ID.GetHashCode() + obj.Status.GetHashCode()).GetHashCode();
    }
}
