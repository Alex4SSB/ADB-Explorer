namespace ADB_Explorer.ViewModels;

public partial class Devices : ObservableObject
{
    #region Full properties

    [ObservableProperty]
    public partial ObservableList<DeviceViewModel> UIList { get; private set; } = [];

    [ObservableProperty]
    public partial LogicalDeviceViewModel? DeviceToOpen { get; set; }
    partial void OnDeviceToOpenChanged(LogicalDeviceViewModel? value)
    {
        if (value is not null)
            DeviceHelper.OpenDevice(value);

        OnPropertyChanged(nameof(Current));
    }

    [ObservableProperty]
    public partial NewDeviceViewModel? DeviceToConnect { get; set; }
    partial void OnDeviceToConnectChanged(NewDeviceViewModel? value)
    {
        if (value is not null)
            DeviceHelper.ConnectDevice(value);
    }

    [ObservableProperty]
    public partial bool IsManualPairingInProgress { get; set; }

    public DateTime LastUpdate { get; set; }

    public List<string> RootDevices { get; protected set; } = [];

    public NewDeviceViewModel CurrentNewDevice { get; set; } = null!;

    public string WsaPort { get; set; } = "";

    #endregion

    #region Read only lists

    public IEnumerable<DeviceViewModel> PrimaryDevices => UIList?.Where(device =>
        device is not MdnsDeviceViewModel
        && device is not NewDeviceViewModel
        && device is not EmulatorPackageDeviceViewModel
        && (device is not LogicalDeviceViewModel logical || logical.Type != DeviceType.Emulator));

    public IEnumerable<DeviceViewModel> EmulatorDevices => UIList?.Where(device =>
        device is EmulatorPackageDeviceViewModel
        || (device is LogicalDeviceViewModel logical && logical.Type == DeviceType.Emulator));

    public IEnumerable<DeviceViewModel> SecondaryDevices => UIList?.Where(device => device is MdnsDeviceViewModel or NewDeviceViewModel);
    public IEnumerable<LogicalDeviceViewModel> LogicalDeviceViewModels => UIList?.OfType<LogicalDeviceViewModel>();
    public IEnumerable<ServiceDeviceViewModel> ServiceDeviceViewModels => UIList?.OfType<ServiceDeviceViewModel>();
    public IEnumerable<HistoryDeviceViewModel> HistoryDeviceViewModels => UIList?.OfType<HistoryDeviceViewModel>();

    #endregion

    #region Emulator package handling

    public void UpdateEmulatorPackages(IEnumerable<EmulatorPackageDeviceViewModel> other) => UpdateEmulatorPackages(UIList, other);

    public static void UpdateEmulatorPackages(ObservableList<DeviceViewModel> self, IEnumerable<EmulatorPackageDeviceViewModel> other)
    {
        self.RemoveAll(thisDevice => thisDevice is EmulatorPackageDeviceViewModel model
            && !model.IsTestDevice
            && !other.Any(otherDevice => otherDevice.AvdName == model.AvdName));

        foreach (var item in other)
        {
            if (self?.Find(thisDevice => thisDevice is EmulatorPackageDeviceViewModel pkg && pkg.AvdName == item.AvdName) is EmulatorPackageDeviceViewModel existing)
            {
                if (existing.Status is DeviceStatus.Offline && item.Status is DeviceStatus.Ok)
                    existing.SetStatus(DeviceStatus.Ok);
            }
            else
            {
                self.Add(item);
            }
        }
    }

    #endregion

    #region Read only properties

    public LogicalDeviceViewModel Current => DeviceToOpen ?? LogicalDeviceViewModels?.FirstOrDefault(device => device.IsOpen);

    public int Count => UIList.Count(d => DeviceHelper.DevicePredicate(d) && d is not HistoryDeviceViewModel and not NewDeviceViewModel);

    #endregion

    public Devices()
    {
        UIList.Add(new MdnsDeviceViewModel(new(), this));
        UIList.Add(new NewDeviceViewModel(new(), this));
        UIList.Add(new WsaPkgDeviceViewModel(new(), this));

        if (Data.Settings.SaveDevices)
            UIList.AddRange(RetrieveHistoryDevices());

        UIList.CollectionChanged += UIList_CollectionChanged;
    }

    private void UIList_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        OnPropertyChanged(nameof(Count));
    }

    #region History device handling

    public IEnumerable<HistoryDeviceViewModel> RetrieveHistoryDevices()
    {
        return Data.Settings.StorageDevices?.Select(d => HistoryDeviceViewModel.FromStorage(d, this)) ?? [];
    }

    public void StoreHistoryDevices() => StoreHistoryDevices(UIList.OfType<HistoryDeviceViewModel>());

    public static void StoreHistoryDevices(IEnumerable<HistoryDeviceViewModel> devices)
    {
        Data.Settings.StorageDevices = [.. devices.Select(h => h.GetStorage())];
    }

    public void AddHistoryDevice(HistoryDeviceViewModel device)
    {
        UIList.Add(device);
        StoreHistoryDevices();
    }

    public void RemoveHistoryDevice(HistoryDeviceViewModel device)
    {
        UIList.Remove(device);
        StoreHistoryDevices();
    }

    #endregion

    #region Service device handling

    /// <summary>Every advertised mDNS service - owned by a device (see <see cref="AssignServices"/>) or listed on its own.</summary>
    private List<ServiceDeviceViewModel> _services = [];

    public IReadOnlyList<ServiceDeviceViewModel> AllServices => _services;

    public void UpdateServices(IEnumerable<ServiceDeviceViewModel> other)
    {
        if (App.AppDispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.Invoke(() => UpdateServices(other));
            return;
        }

        var incoming = other.ToList();
        List<ServiceDeviceViewModel> next = [.. _services.Where(s => s.IsTestDevice && !incoming.Contains(s))];

        // Keep the existing objects - they hold pairing state. Pairing and connect services share a name.
        foreach (var item in incoming)
        {
            if (_services.Find(s => s.ID == item.ID && s.ConnectionKind == item.ConnectionKind) is { } existing)
            {
                existing.UpdateService(item);
                next.Add(existing);
            }
            else
            {
                next.Add(item);
            }
        }

        _services = next;
        AssignServices();
    }

    /// <summary>Gives each service to the online device it advertises; the rest are listed on their own.</summary>
    private void AssignServices()
    {
        var devices = LogicalDeviceViewModels.ToList();
        var owned = devices.ToDictionary(d => d, _ => new List<ServiceDeviceViewModel>());
        List<ServiceDeviceViewModel> unowned = [];

        foreach (var service in _services)
        {
            if (devices.FirstOrDefault(d => d.OwnsService(service)) is { } owner)
                owned[owner].Add(service);
            else
                unowned.Add(service);
        }

        foreach (var (device, services) in owned)
            device.SetServices(services);

        UIList.RemoveAll(d => d is ServiceDeviceViewModel s && !unowned.Contains(s));
        UIList.AddRange(unowned.Where(s => !UIList.Contains(s)));
    }

    public bool ServicesChanged(IEnumerable<ServiceSnapshot> snapshots)
    {
        // if the list is null, we're probably not ready to update
        if (snapshots is null)
            return false;

        var incoming = snapshots.OrderBy(s => s.ID).ThenBy(s => s.ConnectionKind).ToList();
        var existing = _services.Where(s => !s.IsTestDevice).OrderBy(s => s.ID).ThenBy(s => s.ConnectionKind).ToList();

        return existing.Count != incoming.Count
            || existing.Zip(incoming).Any(p =>
                p.First.ID != p.Second.ID
                || p.First.ConnectionKind != p.Second.ConnectionKind
                || p.First.PairingPort != p.Second.Port
                || p.First.MdnsType != p.Second.MdnsType);
    }

    #endregion

    #region Logical device handling

    /// <param name="revivable">Devices no longer listed (e.g. held by past file ops) that may take their transports back.</param>
    public bool UpdateDevices(IEnumerable<LogicalDeviceViewModel> other, IEnumerable<LogicalDeviceViewModel>? revivable = null)
    {
        var result = App.AppDispatcher is not null
            ? App.AppDispatcher.Invoke(() => UpdateDevices(UIList, other, revivable ?? []))
            : UpdateDevices(UIList, other, revivable ?? []);
        ClaimUnclaimedSavedLocations();
        MigrateSavedLocations();
        AssignServices();
        OnPropertyChanged(nameof(Count));

        UpdateLogicalIp();
        UpdateBrandNames();
        UpdateHistoryNames();

        return result;
    }

    private static bool _unclaimedSavedLocationsClaimed;

    /// <summary>
    /// One-time-per-session migration: saved locations from before saved locations were per-device
    /// (<see cref="SavedLocationEntry.DeviceId"/> is <see langword="null"/>) are claimed by the first
    /// device to connect, so they aren't silently lost.
    /// </summary>
    private void ClaimUnclaimedSavedLocations()
    {
        if (_unclaimedSavedLocationsClaimed)
            return;

        var device = LogicalDeviceViewModels.FirstOrDefault(d => d.Status is DeviceStatus.Ok);
        if (device is null)
            return;

        _unclaimedSavedLocationsClaimed = true;
        foreach (var entry in Data.Settings.SavedLocations.Where(e => string.IsNullOrEmpty(e.DeviceId)))
            entry.DeviceId = device.SavedLocationsKey;
    }

    /// <summary>Re-keys saved locations stored under a transport serial to their device's <see cref="LogicalDeviceViewModel.SavedLocationsKey"/>.</summary>
    private void MigrateSavedLocations()
    {
        var saved = Data.Settings.SavedLocations;
        foreach (var device in LogicalDeviceViewModels.ToList())
        {
            var key = device.SavedLocationsKey;
            for (int i = 0; i < saved.Count; i++)
            {
                // Replaced rather than edited: DeviceId isn't observable, and only collection changes get saved.
                if (saved[i].DeviceId != key && device.Matches(saved[i].DeviceId))
                    saved[i] = new(key, saved[i].Path);
            }
        }
    }

    private static bool UpdateDevices(ObservableList<DeviceViewModel> self, IEnumerable<LogicalDeviceViewModel> other, IEnumerable<LogicalDeviceViewModel> revivable)
    {
        bool isCurrentTypeUpdated = false;

        var listed = self.OfType<LogicalDeviceViewModel>().ToList();
        List<LogicalDeviceViewModel> candidates = [.. listed, .. revivable.Where(r => !listed.Contains(r)).Distinct()];
        var incoming = other.SelectMany(source => source.Connections.Select(connection => (Source: source, Connection: connection))).ToList();

        // Nodes 0..candidates-1 are known devices, the rest are listed transports; each cluster is one physical device.
        var clusters = new DisjointSet(candidates.Count + incoming.Count);
        for (int i = 0; i < incoming.Count; i++)
        {
            var (source, connection) = incoming[i];
            var node = candidates.Count + i;

            for (int c = 0; c < candidates.Count; c++)
            {
                if (BelongsTo(candidates[c], source, connection))
                    clusters.Union(node, c);
            }

            for (int j = 0; j < i; j++)
            {
                if (SameDevice(incoming[i], incoming[j]))
                    clusters.Union(node, candidates.Count + j);
            }
        }

        var groups = Enumerable.Range(0, candidates.Count + incoming.Count).GroupBy(clusters.Find);
        foreach (var group in groups)
        {
            var devices = group.Where(n => n < candidates.Count).Select(n => candidates[n]).ToList();
            var connections = group.Where(n => n >= candidates.Count).Select(n => incoming[n - candidates.Count]).ToList();

            if (connections.Count == 0)
            {
                // No longer listed on any transport. Test devices stay; offline status is for the file op mechanism.
                foreach (var gone in devices.Where(d => listed.Contains(d) && !d.IsTestDevice))
                {
                    gone.SetStatus(DeviceStatus.Offline);
                    gone.ClearTransportRoutes();
                    self.Remove(gone);
                }

                continue;
            }

            var preferred = connections.MinBy(c => c.Connection.Rank);
            var device = devices.OrderByDescending(d => d.IsOpen).ThenByDescending(listed.Contains).FirstOrDefault() ?? preferred.Source;

            if (device.IsOpen && device.Status != preferred.Connection.Status)
                isCurrentTypeUpdated = true;

            foreach (var duplicate in devices.Where(d => !ReferenceEquals(d, device)))
            {
                device.Absorb(duplicate);
                self.Remove(duplicate);
            }

            var wasListed = listed.Contains(device);
            device.UpdateDevice(preferred.Source, [.. connections.Select(c => c.Connection).DistinctBy(c => c.ID)]);

            if (wasListed)
                continue;

            self.Add(device);
            if (device.Status is DeviceStatus.Ok)
                Task.Run(() => ShellCommands.FindCommands(device.ID));
        }

        return isCurrentTypeUpdated;
    }

    /// <summary>Whether a listed transport belongs to an already known device.</summary>
    private static bool BelongsTo(LogicalDeviceViewModel device, LogicalDeviceViewModel source, DeviceConnection connection)
    {
        if (ReferenceEquals(device, source))
            return true;

        if (device.IsTestDevice != source.IsTestDevice)
            return false;

        if (device.Connections.Any(c => c.ID == connection.ID) || device.Matches(connection.ID))
            return true;

        if (!string.IsNullOrEmpty(connection.Serial))
        {
            return device.SerialNumber == connection.Serial
                && !device.Connections.Any(c => IpConflicts(c, connection));
        }

        // An IP transport whose serial couldn't be read yet (e.g. unauthorized) still joins by address.
        return connection.Type is DeviceType.Remote
            && device.Type is not DeviceType.Emulator and not DeviceType.WSA
            && device.IsIpAddressValid
            && device.IpAddress == connection.IpAddress;
    }

    private static bool SameDevice((LogicalDeviceViewModel Source, DeviceConnection Connection) a, (LogicalDeviceViewModel Source, DeviceConnection Connection) b)
        => a.Source.IsTestDevice == b.Source.IsTestDevice
           && !string.IsNullOrEmpty(a.Connection.Serial)
           && a.Connection.Serial == b.Connection.Serial
           && !IpConflicts(a.Connection, b.Connection);

    /// <summary>Two IP transports at different addresses are different devices, even with the same (e.g. placeholder) serial.</summary>
    private static bool IpConflicts(DeviceConnection a, DeviceConnection b)
        => a.Type is DeviceType.Remote
           && b.Type is DeviceType.Remote
           && !string.IsNullOrEmpty(a.IpAddress)
           && !string.IsNullOrEmpty(b.IpAddress)
           && a.IpAddress != b.IpAddress;

    private sealed class DisjointSet(int size)
    {
        private readonly int[] _parent = [.. Enumerable.Range(0, size)];

        public int Find(int node)
        {
            while (_parent[node] != node)
                node = _parent[node] = _parent[_parent[node]];

            return node;
        }

        public void Union(int a, int b) => _parent[Find(a)] = Find(b);
    }

    public bool DevicesChanged(IEnumerable<DeviceSnapshot> snapshots)
    {
        if (snapshots is null)
            return false;

        var existing = LogicalDeviceViewModels.Where(d => !d.IsTestDevice).SelectMany(d => d.Connections).OrderBy(c => c.ID).ToList();
        var incoming = snapshots.OrderBy(s => s.ID).ToList();

        return existing.Count != incoming.Count
            || existing.Zip(incoming).Any(p =>
                p.First.ID != p.Second.ID
                || p.First.Type != p.Second.Type
                || p.First.Status != p.Second.Status
                || p.First.DeviceData != p.Second.DeviceData);
    }

    #endregion

    #region General device handling

    public void UpdateDeviceRoot(string deviceID, bool isRootStateKnown)
    {
        if (isRootStateKnown)
        {
            if (!RootDevices.Contains(deviceID))
                RootDevices.Add(deviceID);
        }
        else
        {
            RootDevices.Remove(deviceID);
        }
    }

    public void UpdateHistoryNames()
    {
        if (UpdateHistoryNames(UIList))
            OnPropertyChanged(nameof(UIList));
    }

    public static bool UpdateHistoryNames(ObservableList<DeviceViewModel> devices)
    {
        var result = false;
        foreach (var item in devices.OfType<HistoryDeviceViewModel>().Where(d => string.IsNullOrEmpty(d.DeviceName)))
        {
            var logical = devices.OfType<LogicalDeviceViewModel>().Where(l => l.Type is DeviceType.Remote or DeviceType.Service
                                                                       && l.IpAddress == item.IpAddress);
            if (logical.Any())
            {
                item.SetDeviceName(logical.First().Name);
                StoreHistoryDevices(devices.OfType<HistoryDeviceViewModel>());

                result = true;
            }
        }

        return result;
    }

    public async void UpdateBrandNames()
    {
        // Snapshot before enumerating on a background thread — UIList can be mutated
        // concurrently (device connect/disconnect), which threw InvalidOperationException.
        var devices = UIList.OfType<LogicalDeviceViewModel>().ToList();

        await Task.Run(() => devices.ForEach(d => _ = d.BrandName));

        App.SafeInvoke(() => devices.ForEach(d => d.UpdateName()));
    }

    public async void UpdateLogicalIp()
    {
        if (await UpdateLogicalIp(UIList))
            OnPropertyChanged(nameof(UIList));
    }

    public static async Task<bool> UpdateLogicalIp(ObservableList<DeviceViewModel> devices)
    {
        var result = false;
        var items = devices.OfType<LogicalDeviceViewModel>().Where(d => d.Type is DeviceType.Service or DeviceType.Local && !d.IsIpAddressValid).ToList();
        foreach (var item in items)
        {
            var listed = devices.OfType<ServiceDeviceViewModel>().Where(srv => srv.ID == item.ID || srv.ID == item.BaseID);
            if (item.Services.Concat(listed).FirstOrDefault(srv => srv.IsIpAddressValid) is { } service)
            {
                item.SetIpAddress(service.IpAddress);
                continue;
            }

            await Task.Run(() => result |= AdbService.GetDeviceIp(item));
        }

        return result;
    }

    public bool DevicesAvailable(bool current = false) => AvailableDevices(current).Any();

    private IEnumerable<LogicalDeviceViewModel> AvailableDevices(bool current = false)
    {
        return LogicalDeviceViewModels.Where(
                device => (!current || device.IsOpen)
                && device.Status is DeviceStatus.Ok);
    }

    public bool SetOpenDevice(string selectedId)
        => SetOpenDevice(AvailableDevices().FirstOrDefault(device => device.Matches(selectedId)));

    public static bool SetOpenDevice(LogicalDeviceViewModel? device)
    {
        if (Data.DevicesObject is null)
            return false;

        if (Data.DevicesObject.DeviceToOpen is null && device is null)
            return false;

        if (Data.DevicesObject.DeviceToOpen?.Equals(device) is not true)
            Data.DevicesObject.DeviceToOpen = device;

        if (device is not null)
        {
            Data.Settings.LastDevice = device.Name;
            return true;
        }

        return false;
    }

    #endregion
}
