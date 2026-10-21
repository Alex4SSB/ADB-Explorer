using AdvancedSharpAdbClient.Models;
using System.Net;

namespace ADB_Explorer.ViewModels;

public partial class LogicalDeviceViewModel : DeviceViewModel
{
    #region Full properties

    [ObservableProperty]
    public new partial LogicalDevice Device { get; set; }

    private bool _isOpen;
    /// <summary>
    /// Device is open for browsing
    /// </summary>
    public bool IsOpen
    {
        get => _isOpen;
        private set
        {
            if (SetProperty(ref _isOpen, value) && Root is RootStatus.Enabled)
            {
                if (!value && Data.Settings.UnrootOnDisconnect is true)
                    AdbService.Unroot(Device.ID);
            }
        }
    }

    public DeviceData DeviceData => Device.DeviceData;

    public byte? AndroidVersion => byte.TryParse(AndroidVersionString?.Split('.')[0], out byte ver) ? ver : null;

    [ObservableProperty]
    public partial bool UseIdForName { get; set; }

    [ObservableProperty]
    public partial bool IsEditingName { get; set; }

    [ObservableProperty]
    public partial string? EditedName { get; set; }

    public ObservableList<DriveViewModel> Drives { get; set; } = [];

    [ObservableProperty]
    public partial MountTable Mounts { get; set; } = MountTable.Empty;

    #endregion

    #region Read only properties

    /// <summary>The name set by the user, or <see langword="null"/> when the device shows its regular name.</summary>
    public string? CustomName
    {
        get
        {
            if (Data.Settings?.CustomDeviceNames is { } names && names.TryGetValue(SavedLocationsKey, out var name) && !string.IsNullOrWhiteSpace(name))
                return name;

            return null;
        }
    }

    public bool HasCustomName => CustomName is not null;

    public string Name => CustomName ?? DefaultName;

    /// <summary>The device name derived from the device itself, ignoring <see cref="CustomName"/>.</summary>
    public string DefaultName
    {
        get
        {
            if (Device.Type is DeviceType.Emulator && !string.IsNullOrEmpty(Device.AvdName))
                return Device.AvdName;

            if (!UseIdForName && !string.IsNullOrEmpty(BrandName))
                return BrandName;

            // Prevent displaying [Service] for offline connect services acquired via adb devices -l upon first contact
            if (Device.Type is DeviceType.Service
                && string.IsNullOrEmpty(Device.Name)
                && DateTime.Now - DiscoverTime < AdbExplorerConst.SERVICE_DISPLAY_DELAY)
                return " ";

            if (UseIdForName)
                return Device.ID;

            return string.IsNullOrEmpty(Device.Name) ? Device.ID : Device.Name;
        }
    }

    public string AvdName => Device.AvdName ?? "";

    public string BaseID => Connections.FirstOrDefault(c => c.Type is DeviceType.Service)?.ID.Split('.')[0] ?? ID;

    public IReadOnlyList<DeviceConnection> Connections => Device.Connections;

    public bool HasUsbConnection => Connections.Any(c => c.IsUsb);

    public bool HasWifiConnection => Connections.Any(c => c.IsWifi);

    public bool HasMdnsConnection => Connections.Any(c => c.Type is DeviceType.Service);

    /// <summary>USB, mDNS and IP devices show an icon per transport instead of <see cref="DeviceViewModel.TypeIconContent"/>.</summary>
    public bool ShowsConnectionIcons => Connections.All(c => c.Type is DeviceType.Local or DeviceType.Service or DeviceType.Remote);

    /// <summary>The USB version actually negotiated, e.g. <c>2.0</c>.</summary>
    [ObservableProperty]
    public partial string? UsbVersion { get; private set; }

    /// <summary>Wi-Fi rate the device sends at, so pulls are bound by it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiLinkSpeed))]
    public partial int? WifiTxMbps { get; private set; }

    /// <summary>Wi-Fi rate the device receives at, so pushes are bound by it.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WifiLinkSpeed))]
    public partial int? WifiRxMbps { get; private set; }

    /// <summary>The faster Wi-Fi direction in the network's own (untranslated) unit, e.g. <c>1200 Mbps</c>.</summary>
    public string? WifiLinkSpeed
    {
        get
        {
            var mbps = Math.Max(WifiTxMbps ?? 0, WifiRxMbps ?? 0);

            return mbps > 0 ? $"{mbps} Mbps" : null;
        }
    }

    /// <summary>Transports whose speed was read - once each, until they disconnect.</summary>
    private readonly HashSet<string> _measuredConnections = [];

    private void MeasureNewConnections()
    {
        _measuredConnections.RemoveWhere(id => Connections.All(c => c.ID != id));

        if (IsTestDevice)
            return;

        foreach (var connection in Connections.Where(c => c.Status is DeviceStatus.Ok))
        {
            if (!_measuredConnections.Add(connection.ID))
                continue;

            if (connection.Type is DeviceType.Local)
                Task.Run(() => UsbVersion = NativeMethods.GetUsbLinkVersion(connection.ID));
            else if (connection.IsWifi)
                Task.Run(RefreshWifiSpeeds);
        }
    }

    /// <summary>Re-reads the Wi-Fi rates, which shift with the signal. Blocks on adb, so call it off the UI thread.</summary>
    public void RefreshWifiSpeeds()
    {
        if (IsTestDevice || !Connections.Any(c => c.IsWifi && c.Status is DeviceStatus.Ok))
            return;

        var (tx, rx) = AdbService.GetWifiLinkSpeeds(ID);

        // A failed read keeps the last rates rather than blanking them
        if (tx is null && rx is null)
            return;

        WifiTxMbps = tx;
        WifiRxMbps = rx;
    }

    /// <summary>Every transport serial this device has been listed under, so IDs stored earlier still resolve to it.</summary>
    private readonly HashSet<string> _knownIds = new(StringComparer.Ordinal);

    [ObservableProperty]
    public partial IReadOnlyList<ServiceDeviceViewModel> Services { get; private set; } = [];

    public void SetServices(List<ServiceDeviceViewModel> services)
    {
        if (!Services.SequenceEqual(services))
            Services = services;
    }

    /// <summary>An online device owns the mDNS services it advertises: by the serial in their name, or by IP.</summary>
    public bool OwnsService(ServiceDeviceViewModel service)
    {
        if (Status is DeviceStatus.Offline || IsTestDevice != service.IsTestDevice)
            return false;

        if (service.ID.StartsWith("adb-", StringComparison.Ordinal) && DeviceHelper.ParseMdnsSerial(service.ID) == SerialNumber)
            return true;

        return IsIpAddressValid && IpAddress == service.IpAddress;
    }

    /// <summary>IDs of duplicate objects merged into this one; their commands follow this device's transport too.</summary>
    private readonly List<string> _absorbedIds = [];

    public void Absorb(LogicalDeviceViewModel duplicate)
    {
        _absorbedIds.Add(duplicate.ID);
        _absorbedIds.AddRange(duplicate._absorbedIds);
        lock (_knownIds)
        {
            _knownIds.Add(duplicate.ID);
            _knownIds.UnionWith(duplicate._knownIds);
        }
    }

    public void ClearTransportRoutes()
    {
        foreach (var id in _absorbedIds.Prepend(ID))
            AdbService.RemoveTransportRoute(id);
    }

    public bool Matches(string? id)
    {
        if (string.IsNullOrEmpty(id))
            return false;

        if (id == ID || id == SerialNumber)
            return true;

        // Updated on the UI thread, looked up from background ones too
        lock (_knownIds)
            return _knownIds.Contains(id);
    }

    /// <summary>Saved locations are keyed by serial, so they follow the device across transports and sessions.</summary>
    public string SavedLocationsKey => string.IsNullOrEmpty(SerialNumber) ? ID : SerialNumber;

    private string _serialNumber = "";
    /// <summary>
    /// Stable device identifier used for comparison and on-disk storage (USB ID, <c>ro.serialno</c>, mDNS serial, or AVD name).
    /// </summary>
    public string SerialNumber
    {
        get
        {
            if (Type is DeviceType.Remote && string.IsNullOrEmpty(_serialNumber))
                RefreshSerialNumber();

            return _serialNumber;
        }
    }

    public RootStatus Root => Device.Root;

    private ShellIdentity? _shellIdentity;
    public ShellIdentity? ShellIdentity => _shellIdentity;

    public ShellIdentity? GetOrLoadShellIdentity()
    {
        _shellIdentity ??= AdbService.GetShellIdentity(ID);
        return _shellIdentity;
    }

    public void RefreshShellIdentity()
    {
        _shellIdentity = AdbService.GetShellIdentity(ID);
        OnPropertyChanged(nameof(HasRootShell));
    }

    public void SetShellIdentity(ShellIdentity? identity)
    {
        _shellIdentity = identity;
        OnPropertyChanged(nameof(HasRootShell));
    }

    public bool HasRootShell => ShellIdentity?.IsRoot == true;

    private readonly object _unixIdLock = new();
    private readonly HashSet<string> _knownUsers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _knownGroups = new(StringComparer.Ordinal);
    private bool _unixIdsLoaded;

    public void RecordUnixIdentity(string? user, string? group)
    {
        lock (_unixIdLock)
        {
            if (!string.IsNullOrEmpty(user))
                _knownUsers.Add(user);
            if (!string.IsNullOrEmpty(group))
                _knownGroups.Add(group);
        }
    }

    public void EnsureKnownIdentities()
    {
        lock (_unixIdLock)
        {
            if (_unixIdsLoaded)
                return;
        }

        var (users, groups) = AdbService.GetKnownUsersAndGroups(ID);
        lock (_unixIdLock)
        {
            foreach (var user in users)
                _knownUsers.Add(user);
            foreach (var group in groups)
                _knownGroups.Add(group);
            _unixIdsLoaded = true;
        }
    }

    public List<string> GetKnownUsersOrdered(string? current)
        => OrderedUnixIdentities(_knownUsers, current);

    public List<string> GetKnownGroupsOrdered(string? current)
        => OrderedUnixIdentities(_knownGroups, current);

    private List<string> OrderedUnixIdentities(HashSet<string> source, string? current)
    {
        lock (_unixIdLock)
        {
            var list = source.ToList();
            if (!string.IsNullOrEmpty(current) && !source.Contains(current))
                list.Add(current);

            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }
    }

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

    /// <summary>Failures aren't cached, but retrying on every access re-runs getprop each time
    /// (Name reads this, often on the UI thread) - so wait between attempts.</summary>
    private static readonly TimeSpan PropsRetryDelay = TimeSpan.FromSeconds(10);

    private readonly object _propsLock = new();

    private DateTime _propsFailedAt = DateTime.MinValue;

    public Dictionary<string, string> Props
    {
        get
        {
            if (field is not null)
                return field;

            var loadedHere = false;

            // Single flight: concurrent first readers (UI thread, InitDevice, UpdateBrandNames)
            // wait for one getprop instead of each running their own.
            lock (_propsLock)
            {
                if (field is null)
                {
                    if (DateTime.Now - _propsFailedAt < PropsRetryDelay)
                        return [];

                    int exitCode = AdbService.ExecuteDeviceAdbShellCommand(ID, AdbService.GET_PROP, out string stdout, out string stderr, CancellationToken.None);
                    if (exitCode != 0)
                    {
                        _propsFailedAt = DateTime.Now;
                        return [];
                    }

                    field = stdout.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Where(
                        l => l[0] == '[' && l[^1] == ']').TryToDictionary(
                            line => line.Split(':')[0].Trim('[', ']', ' '),
                            line => line.Split(':')[1].Trim('[', ']', ' '));

                    loadedHere = true;
                }
            }

            // Outside the lock: raising PropertyChanged may marshal to a UI thread that is itself waiting on it.
            if (loadedHere)
            {
                RefreshSerialNumber();
                OnPropertyChanged(nameof(AndroidVersion));
            }

            return field;
        }
    }

    public string? BrandName
    {
        get
        {
            if (Device.Type is DeviceType.Emulator)
                return Device.AvdName;

            if (string.IsNullOrEmpty(field))
            {
                var props = Props;
                field = props.GetValueOrDefault(AdbService.BRAND_NAME) ?? props.GetValueOrDefault(AdbService.HOST_NAME);
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
                field = Props.GetValueOrDefault(AdbService.ANDROID_VERSION, "");
            }
            return field;
        }
    } = "";

    public HashSet<string> AdbFeatures
    {
        get
        {
            field ??= AdbService.GetDeviceFeatures(DeviceData);
            return field;
        }
    }

    public bool SupportsSyncV2 => AdbFeatures.Contains(AdbService.FEATURE_SEND_RECV_V2);

    public bool SupportsLsV2 => AdbFeatures.Contains(AdbService.FEATURE_LS_V2);

    #endregion

    public static implicit operator string(LogicalDeviceViewModel vm) => vm?.ID;

    public DateTime DiscoverTime { get; }

    #region Commands

    public DeviceAction BrowseCommand { get; }
    public DeviceAction RemoveCommand { get; }
    public DeviceAction ToggleRootCommand { get; }
    public DeviceAction SideloadCommand { get; }
    public DeviceAction EditNameCommand { get; }
    public DeviceAction ClearNameCommand { get; }
    public List<RebootCommand> RebootCommands { get; } = [];

    #endregion

    public LogicalDeviceViewModel(LogicalDevice device) : base(device)
    {
        DiscoverTime = DateTime.Now;

        Device = device;

        foreach (var connection in device.Connections)
            _knownIds.Add(connection.ID);

        InitDeviceDrives();
        RefreshSerialNumber(allowPropLookup: false);

        // Not gated on !IsOpen - browsing an already-open device just points the active tab
        // at it again, so there's no reason to disable this then.
        BrowseCommand = new(() => device.Status is DeviceStatus.Ok && device.Type is not DeviceType.Sideload,
                            () => DeviceHelper.BrowseDeviceAction(this));

        RemoveCommand = DeviceHelper.RemoveDeviceCommand(this);

        ToggleRootCommand = DeviceHelper.ToggleRootDeviceCommand(this);

        EditNameCommand = new(() => true, ToggleNameEdit);
        ClearNameCommand = new(() => true, ClearCustomName);

        App.SafeInvoke(() =>
        {
            Thread.CurrentThread.CurrentCulture = Data.Settings.ActualFormatCulture;

            foreach (RebootCommand.RebootType type in Enum.GetValues<RebootCommand.RebootType>())
                RebootCommands.Add(new RebootCommand(this, type));
        });

        SideloadCommand = new(() => Device.Type is DeviceType.Sideload or DeviceType.Recovery && device.Status is DeviceStatus.Ok,
                              () => DeviceHelper.SideloadDeviceAction(this));

        if (Data.DevicesObject is not null)
            Data.DevicesObject.PropertyChanged += DevicesObject_PropertyChanged;
    }

    private void DevicesObject_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(Devices.DeviceToOpen))
        {
            IsOpen = Data.DevicesObject?.DeviceToOpen?.ID == ID;

            if (!IsOpen)
                OnPropertyChanged(nameof(AndroidVersion));
        }
    }

    #region Setter functions

    public void SetAndroidVersion()
    {
        if (!IsOpen)
            return;

        OnPropertyChanged(nameof(AndroidVersion));
    }

    /// <summary>
    /// Takes the transports this device is currently listed on; <paramref name="source"/> is the freshly
    /// listed device of the preferred one, for its name.
    /// </summary>
    public void UpdateDevice(LogicalDeviceViewModel source, IReadOnlyCollection<DeviceConnection> connections)
    {
        if (Device.Type is DeviceType.Emulator)
        {
            if (!string.IsNullOrEmpty(source.Device.AvdName))
                Device.AvdName = source.Device.AvdName;
        }
        else if (string.IsNullOrEmpty(BrandName))
        {
            Device.Name = source.Device.Name;
        }

        SetConnections(connections);

        if (Status is DeviceStatus.Offline)
            SetAvdName(null);
    }

    public void SetConnections(IReadOnlyCollection<DeviceConnection> connections)
    {
        var previousType = Type;
        var previousIp = IpAddress;

        Device.SetConnections(connections);

        lock (_knownIds)
            _knownIds.UnionWith(connections.Select(c => c.ID));

        foreach (var id in _absorbedIds.Prepend(ID))
            AdbService.SetTransportRoute(id, Device.Primary.ID);

        RefreshSerialNumber();
        SetStatus(Device.Primary.Status);

        OnPropertyChanged(nameof(Connections));
        OnPropertyChanged(nameof(HasUsbConnection));
        OnPropertyChanged(nameof(HasWifiConnection));
        OnPropertyChanged(nameof(HasMdnsConnection));
        OnPropertyChanged(nameof(ShowsConnectionIcons));
        OnPropertyChanged(nameof(DeviceData));
        OnPropertyChanged(nameof(BaseID));

        MeasureNewConnections();

        if (previousIp != IpAddress)
        {
            OnPropertyChanged(nameof(IpAddress));
            OnPropertyChanged(nameof(IsIpAddressValid));
        }

        if (previousType == Type)
            return;

        OnPropertyChanged(nameof(Type));
        OnPropertyChanged(nameof(TypeIcon));
        OnPropertyChanged(nameof(TypeIconContent));
        OnPropertyChanged(nameof(Tooltip));
    }

    public string? GetAvdNameFromProps()
    {
        if (Device.Type is not DeviceType.Emulator)
            return null;

        return Props.GetValueOrDefault(AdbService.QEMU_BOOT_AVD_NAME)
            ?? Props.GetValueOrDefault(AdbService.QEMU_KERNEL_AVD_NAME);
    }

    public void SetAvdName(string? avdName)
    {
        if (Device.AvdName == avdName)
            return;

        Device.AvdName = avdName;
        OnPropertyChanged(nameof(AvdName));
        OnPropertyChanged(nameof(BrandName));

        UpdateName();
        RefreshSerialNumber();
    }

    private void RefreshSerialNumber(bool allowPropLookup = true)
    {
        var next = ResolveSerialNumber(allowPropLookup);
        if (_serialNumber == next)
            return;

        _serialNumber = next;
    }

    private string ResolveSerialNumber(bool allowPropLookup)
    {
        if (Type is DeviceType.Emulator)
            return !string.IsNullOrEmpty(AvdName) ? AvdName : ID;

        if (Type is DeviceType.WSA)
            return ID;

        if (Connections.FirstOrDefault(c => !string.IsNullOrEmpty(c.Serial)) is { } known)
            return known.Serial;

        return Type switch
        {
            DeviceType.Remote when !allowPropLookup => GetRemoteSerialFallback(ID),
            DeviceType.Remote => Props.GetValueOrDefault(AdbService.SERIAL_NO) ?? GetRemoteSerialFallback(ID),
            _ => ID,
        };
    }

    private static string GetRemoteSerialFallback(string id)
    {
        if (!id.Contains(':'))
            return "";

        var host = id.Split(':')[0];
        return IPAddress.TryParse(host, out _) ? "" : host;
    }

    public void EnableRoot(bool enable)
    {
        Device.Root = enable
            ? AdbService.Root(Device.ID) ? RootStatus.Enabled : RootStatus.Forbidden
            : AdbService.Unroot(Device.ID) ? RootStatus.Disabled : RootStatus.Unchecked;

        RefreshShellIdentity();

        if (Data.ActiveDevice?.ID == ID)
            AdbHelper.ApplyMountInfo(this, Data.DeviceCts.Token);

        OnPropertyChanged(nameof(Root));
    }

    public void InvalidateRootStatus()
    {
        // Runs on every status change - a device that just came online deserves a fresh attempt.
        _propsFailedAt = DateTime.MinValue;

        SetShellIdentity(null);

        if (Root is RootStatus.Unchecked)
            return;

        Device.Root = RootStatus.Unchecked;
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(RootString));
    }

    public bool SetRootStatus(RootStatus status)
    {
        if (Root == status)
            return false;

        Device.Root = status;
        OnPropertyChanged(nameof(Root));
        OnPropertyChanged(nameof(RootString));

        if (Data.ActiveDevice?.ID == ID && Status is DeviceStatus.Ok)
        {
            RefreshShellIdentity();
            AdbHelper.ApplyMountInfo(this, Data.DeviceCts.Token);
        }

        return true;
    }

    public void UpdateBattery(CancellationToken cancellationToken)
    {
        Device.Battery.Update(AdbService.GetBatteryInfo(this, cancellationToken));
    }

    public void UpdateName()
    {
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(HasCustomName));
    }

    /// <summary>Starts editing the name; when already editing, applies the edit.</summary>
    private void ToggleNameEdit()
    {
        if (!IsEditingName)
        {
            EditedName = Name;
            IsEditingName = true;
            return;
        }

        var text = EditedName?.Trim() ?? "";
        if (text.Length == 0 || text == DefaultName)
            Data.Settings.CustomDeviceNames.Remove(SavedLocationsKey);
        else
            Data.Settings.CustomDeviceNames[SavedLocationsKey] = text;

        IsEditingName = false;
        UpdateName();
    }

    private void ClearCustomName()
    {
        Data.Settings.CustomDeviceNames.Remove(SavedLocationsKey);

        IsEditingName = false;
        UpdateName();
    }

    public void CancelNameEdit() => IsEditingName = false;

    #endregion

    #region Drive handling

    private void InitDeviceDrives() => App.SafeInvoke(InitDeviceDrivesCore);

    private void InitDeviceDrivesCore()
    {
        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Root).Key)));
        Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Internal).Key)));

        Drives.Add(new VirtualDriveViewModel(new(path: AdbLocation.StringFromLocation(Navigation.SpecialLocation.RecycleBin), itemsCount: null)));
        Drives.Add(new VirtualDriveViewModel(new(path: AdbExplorerConst.TEMP_PATH)));
        Drives.Add(new VirtualDriveViewModel(new(path: AdbLocation.StringFromLocation(Navigation.SpecialLocation.PackageDrive))));
    }

    internal void EnsureDefaultDrives() => App.SafeInvoke(EnsureDefaultDrivesCore);

    private void EnsureDefaultDrivesCore()
    {
        if (Drives.Any(d => d.Type is AbstractDrive.DriveType.Internal))
            return;

        if (Drives.Count == 0)
            InitDeviceDrivesCore();
        else
            Drives.Add(new LogicalDriveViewModel(new(path: AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is AbstractDrive.DriveType.Internal).Key)));
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
        var mountTask = Task.Run(() => AdbService.GetRemovableDriveInfo(ID));
        mountTask.ContinueWith(t =>
        {
            if (t.IsCanceled)
                return;

            dispatcher.BeginInvoke(() => ApplyRemovableDriveInfo(t.Result));
        });
    }

    /// <summary>
    /// Classifies every drive still of unknown type as SD/expansion or USB/OTG (with its manufacturer
    /// and volume label when known), using the mount info gathered by <see cref="AdbService.GetRemovableDriveInfo"/>.
    /// Drives with no matching mount info default to <see cref="AbstractDrive.DriveType.External"/>.
    /// </summary>
    private void ApplyRemovableDriveInfo(Dictionary<string, AdbService.DriveMountInfo> mountInfo)
    {
        foreach (var drive in Drives.OfType<LogicalDriveViewModel>().Where(d => d.Type is AbstractDrive.DriveType.Unknown))
        {
            if (mountInfo.TryGetValue(drive.Path, out var info))
                drive.SetRemovableInfo(info.Type, info.Manufacturer, info.VolumeLabel);
            else
                drive.SetType(AbstractDrive.DriveType.External);
        }
    }

    private async Task<bool> UpdateExtensionDrivesAsync(IEnumerable<DriveSnapshot> snapshots, Dispatcher dispatcher)
    {
        var list = snapshots.ToList();

        await Task.Run(() =>
        {
            var mountInfo = AdbService.GetRemovableDriveInfo(ID);

            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Type is not AbstractDrive.DriveType.Unknown)
                    continue;

                list[i] = mountInfo.TryGetValue(list[i].Path, out var info)
                    ? list[i] with { Type = info.Type, Manufacturer = info.Manufacturer, VolumeLabel = info.VolumeLabel }
                    : list[i] with { Type = AbstractDrive.DriveType.External };
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
            if (snapshot.Type is AbstractDrive.DriveType.Temp)
            {
                if (Drives.OfType<VirtualDriveViewModel>().FirstOrDefault(d => d.Type is AbstractDrive.DriveType.Temp) is VirtualDriveViewModel temp)
                    temp.UpdateDrive(snapshot);
                continue;
            }

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

    #endregion
}
