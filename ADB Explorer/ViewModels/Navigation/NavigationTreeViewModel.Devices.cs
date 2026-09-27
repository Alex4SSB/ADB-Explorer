namespace ADB_Explorer.ViewModels;

public partial class NavigationTreeViewModel
{
    /// <summary>The active tab's device. Not <see cref="LogicalDeviceViewModel.IsOpen"/> or
    /// <see cref="Devices.Current"/>, which are app-wide and don't follow tab switches.</summary>
    private static LogicalDeviceViewModel? ActiveDevice => Data.ActiveExplorerInstance.EffectiveDevice;

    private static bool IsActiveDevice(LogicalDeviceViewModel? device)
        => device is not null && ActiveDevice?.ID == device.ID;

    private NavigationTreeNode? CurrentDeviceNode
        => TreeSource.FirstOrDefault(node => IsActiveDevice(node.Device));

    private static bool IsOpenedDevice(LogicalDeviceViewModel device)
        => IsActiveDevice(device);

    private static IEnumerable<LogicalDeviceViewModel> ConnectedDevices()
    {
        var devices = Data.DevicesObject?.LogicalDeviceViewModels;
        if (devices is null)
            return [];

        return devices.Where(device =>
            (device.Status is DeviceStatus.Ok || device.IsOpen || IsActiveDevice(device))
            && DeviceHelper.DevicePredicate(device));
    }

    private void SyncDeviceRoots()
    {
        var devices = ConnectedDevices().ToList();
        if (devices.Count == 0)
        {
            ClearTree();
            return;
        }

        var remaining = new HashSet<NavigationTreeNode>();
        foreach (var device in devices)
        {
            var node = FindDeviceNode(device);
            if (node is null)
            {
                node = CreateDeviceNode(device);
                TreeSource.Add(node);
            }
            else
            {
                node.DisplayName = device.Name;
            }

            SyncSavedLocationsForDevice(node, device);
            SyncDrivesForDevice(node, device);
            remaining.Add(node);
        }

        foreach (var stale in TreeSource.Where(node => !remaining.Contains(node)).ToList())
        {
            if (ReferenceEquals(_selectedTreeNode, stale) || IsAncestor(_selectedTreeNode, stale))
                SelectTreeNode(null);

            stale.Detach();
            TreeSource.Remove(stale);
        }

        for (var i = 0; i < devices.Count; i++)
        {
            var node = FindDeviceNode(devices[i]);
            if (node is null)
                continue;

            var currentIndex = TreeSource.IndexOf(node);
            if (currentIndex >= 0 && currentIndex != i)
                TreeSource.Move(currentIndex, i);
        }

        UpdateDeviceSeparators();
    }

    private void UpdateDeviceSeparators()
    {
        var last = TreeSource.Count - 1;
        for (var i = 0; i < TreeSource.Count; i++)
            TreeSource[i].ShowSeparator = i < last;
    }

    private NavigationTreeNode? FindDeviceNode(LogicalDeviceViewModel device)
        => TreeSource.FirstOrDefault(node => node.Device == device);

    private void SyncDrivesForDevice(NavigationTreeNode deviceNode, LogicalDeviceViewModel device)
    {
        var drives = VisibleDrives(device).ToList();
        var remaining = new HashSet<NavigationTreeNode>();
        foreach (var drive in drives)
        {
            var node = deviceNode.Children.FirstOrDefault(n => n.Drive == drive)
                ?? deviceNode.Children.FirstOrDefault(n => n.Drive is not null && n.Drive.Path == drive.Path);

            if (node is null)
            {
                node = CreateDriveNode(drive, device);
                InsertDrive(deviceNode, node);
            }
            else if (node.Drive != drive)
            {
                node.Detach();
                var index = deviceNode.Children.IndexOf(node);
                var replacement = CreateDriveNode(drive, device);
                foreach (var child in node.Children.ToList())
                    replacement.Children.Add(child);
                node.Children.Clear();
                deviceNode.Children[index] = replacement;
                node = replacement;
            }

            remaining.Add(node);
        }

        foreach (var stale in deviceNode.Children.Where(n => !n.IsSavedLocation && !remaining.Contains(n)).ToList())
        {
            if (ReferenceEquals(_selectedTreeNode, stale) || IsAncestor(_selectedTreeNode, stale))
                SelectTreeNode(null);

            stale.Detach();
            deviceNode.Children.Remove(stale);
        }

        // InsertDrive doesn't know about saved-location nodes (it may place a new drive ahead of
        // one), so the full order - saved locations first, then drives by type - is rebuilt here.
        var ordered = deviceNode.Children.Where(n => n.IsSavedLocation)
            .Concat(deviceNode.Children.Where(n => !n.IsSavedLocation).OrderBy(n => n.Drive?.Type))
            .ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var currentIndex = deviceNode.Children.IndexOf(ordered[i]);
            if (currentIndex != i)
                deviceNode.Children.Move(currentIndex, i);
        }
    }

    /// <summary>Saved-location nodes always occupy the front of the device node's children, ahead of drives.</summary>
    private void SyncSavedLocationsForDevice(NavigationTreeNode deviceNode, LogicalDeviceViewModel device)
    {
        var entries = Data.Settings.SavedLocations.Where(e => e.DeviceId == device.ID).ToList();
        var remaining = new HashSet<NavigationTreeNode>();

        for (var i = 0; i < entries.Count; i++)
        {
            var node = deviceNode.Children.FirstOrDefault(n =>
                n.IsSavedLocation && NavigationTreeNode.PathsEqual(n.Path, entries[i].Path));

            if (node is null)
            {
                node = CreateSavedLocationNode(entries[i].Path, device);
                deviceNode.Children.Insert(Math.Min(i, deviceNode.Children.Count), node);
            }

            remaining.Add(node);

            var currentIndex = deviceNode.Children.IndexOf(node);
            if (currentIndex != i)
                deviceNode.Children.Move(currentIndex, i);
        }

        foreach (var stale in deviceNode.Children.Where(n => n.IsSavedLocation && !remaining.Contains(n)).ToList())
        {
            if (ReferenceEquals(_selectedTreeNode, stale) || IsAncestor(_selectedTreeNode, stale))
                SelectTreeNode(null);

            stale.Detach();
            deviceNode.Children.Remove(stale);
        }
    }

    /// <summary>
    /// Basename only - skips archive-boundary detection (a live ADB stat) since the last path
    /// segment is right either way. Icon is replaced by a star overlay in the tree's DataTemplate.
    /// </summary>
    private NavigationTreeNode CreateSavedLocationNode(string path, LogicalDeviceViewModel device)
    {
        var name = FileHelper.GetFullName(path);
        var icon = new FileClass(name, path, AbstractFile.FileType.Folder).Icon;

        var node = new NavigationTreeNode(
            path,
            name,
            icon,
            OnTreeNodeSelected,
            ownerDevice: device)
        {
            IsSavedLocation = true,
            CanExpand = false,
        };
        node.CutState = CutStateFor(node);
        return node;
    }

    private NavigationTreeNode CreateDeviceNode(LogicalDeviceViewModel device)
        => new(
            AdbLocation.StringFromLocation(Navigation.SpecialLocation.DriveView),
            device.Name,
            NavigationTreeNode.DeviceIcon(),
            OnTreeNodeSelected,
            device: device)
        {
            IsExpanded = IsOpenedDevice(device)
        };

    private NavigationTreeNode CreateDriveNode(DriveViewModel drive, LogicalDeviceViewModel device)
    {
        Action<NavigationTreeNode>? onExpanded = null;
        if (drive.Type is not AbstractDrive.DriveType.Package
            && drive.Type is not AbstractDrive.DriveType.Trash)
            onExpanded = OnTreeNodeExpanded;

        if (drive is VirtualDriveViewModel { Type: AbstractDrive.DriveType.Trash, ItemsCount: null }
            && Data.Settings.EnableRecycle)
            TrashHelper.UpdateRecycledItemsCount(device);

        var node = new NavigationTreeNode(
            drive.Path,
            drive.DisplayName,
            NavigationTreeNode.DriveIcon(drive),
            OnTreeNodeSelected,
            drive,
            ownerDevice: device,
            onExpanded: onExpanded);
        node.CutState = CutStateFor(node);
        return node;
    }

    private static void InsertDrive(NavigationTreeNode deviceNode, NavigationTreeNode driveNode)
    {
        var index = 0;
        while (index < deviceNode.Children.Count && CompareDriveNodes(deviceNode.Children[index], driveNode) < 0)
            index++;

        deviceNode.Children.Insert(index, driveNode);
    }

    private static int CompareDriveNodes(NavigationTreeNode left, NavigationTreeNode right)
    {
        var leftType = (int)(left.Drive?.Type ?? AbstractDrive.DriveType.Unknown);
        var rightType = (int)(right.Drive?.Type ?? AbstractDrive.DriveType.Unknown);
        return leftType.CompareTo(rightType);
    }

    private static DriveViewModel? ResolveTreeDrive(string path)
    {
        var location = AdbLocation.LocationFromString(path);
        if (location is Navigation.SpecialLocation.RecycleBin)
            return FindDrive(AbstractDrive.DriveType.Trash);

        if (location is Navigation.SpecialLocation.PackageDrive)
            return FindDrive(AbstractDrive.DriveType.Package);

        if (IsRecycleLocation(path))
            return FindDrive(AbstractDrive.DriveType.Trash);

        if (path == AdbExplorerConst.TEMP_PATH
            || path.StartsWith($"{AdbExplorerConst.TEMP_PATH}/", StringComparison.Ordinal))
            return FindDrive(AbstractDrive.DriveType.Temp);

        if (Data.FileActions.IsAppDrive)
            return FindDrive(AbstractDrive.DriveType.Package);

        if (Data.FileActions.IsRecycleBin)
            return FindDrive(AbstractDrive.DriveType.Trash);

        if (Data.FileActions.IsTemp)
            return FindDrive(AbstractDrive.DriveType.Temp);

        return DriveHelper.GetCurrentDrive(path, ActiveDevice);
    }

    private static DriveViewModel? FindDrive(AbstractDrive.DriveType type)
        => ActiveDevice?.Drives.FirstOrDefault(d => d.Type == type);

    private static bool IsRecycleLocation(string path)
        => AdbExplorerConst.POSSIBLE_RECYCLE_PATHS.Any(recycle =>
            path == recycle || path.StartsWith($"{recycle}/", StringComparison.Ordinal));

    private static IEnumerable<DriveViewModel> VisibleDrives(LogicalDeviceViewModel device)
    {
        return device.Drives.Where(drive => drive.Type switch
        {
            AbstractDrive.DriveType.Trash => Data.Settings.EnableRecycle,
            AbstractDrive.DriveType.Temp or AbstractDrive.DriveType.Package => Data.Settings.EnableApk,
            _ => true,
        });
    }
}
