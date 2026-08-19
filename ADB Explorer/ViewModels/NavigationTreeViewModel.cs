using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public partial class NavigationTreeViewModel : ObservableObject
{
    private readonly Func<IEnumerable<IBrowserItem>?> _currentExplorerItems;
    private readonly HashSet<ObservableList<DriveViewModel>> _subscribedDriveLists = [];
    private NavigationTreeNode? _selectedTreeNode;
    private bool _syncing;

    [ObservableProperty]
    public partial ObservableList<NavigationTreeNode> TreeSource { get; set; } = [];

    public NavigationTreeViewModel(Func<IEnumerable<IBrowserItem>?> currentExplorerItems)
    {
        _currentExplorerItems = currentExplorerItems;
    }

    public void Sync()
    {
        App.SafeInvoke(() =>
        {
            _syncing = true;
            try
            {
                SyncDeviceRoots();
                DiscoverAndSelect(Data.CurrentPath);
            }
            finally
            {
                _syncing = false;
            }
        });
    }

    public void OnShowHiddenItemsChanged()
    {
        InvalidateTreeChildrenLoaded();
        ReloadExpandedTreeFolders();
        Sync();
    }

    public void SubscribeDriveLists()
    {
        var lists = ConnectedDevices().Select(device => device.Drives).ToHashSet();

        foreach (var old in _subscribedDriveLists.Where(list => !lists.Contains(list)).ToList())
        {
            old.CollectionChanged -= Drives_CollectionChanged;
            _subscribedDriveLists.Remove(old);
        }

        foreach (var list in lists)
        {
            if (_subscribedDriveLists.Add(list))
                list.CollectionChanged += Drives_CollectionChanged;
        }
    }

    private void Drives_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Sync();

    private NavigationTreeNode? CurrentDeviceNode
        => TreeSource.FirstOrDefault(node => node.Device?.IsOpen == true)
        ?? TreeSource.FirstOrDefault(node => node.Device == Data.DevicesObject?.Current);

    private static bool IsOpenedDevice(LogicalDeviceViewModel device)
        => device.IsOpen || device == Data.DevicesObject?.Current;

    private static IEnumerable<LogicalDeviceViewModel> ConnectedDevices()
    {
        var devices = Data.DevicesObject?.LogicalDeviceViewModels;
        if (devices is null)
            return [];

        return devices.Where(device =>
            (device.Status is DeviceStatus.Ok || device.IsOpen)
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

        foreach (var stale in deviceNode.Children.Where(n => !remaining.Contains(n)).ToList())
        {
            if (ReferenceEquals(_selectedTreeNode, stale) || IsAncestor(_selectedTreeNode, stale))
                SelectTreeNode(null);

            stale.Detach();
            deviceNode.Children.Remove(stale);
        }

        var ordered = deviceNode.Children.OrderBy(n => n.Drive?.Type).ToList();
        for (var i = 0; i < ordered.Count; i++)
        {
            var currentIndex = deviceNode.Children.IndexOf(ordered[i]);
            if (currentIndex != i)
                deviceNode.Children.Move(currentIndex, i);
        }
    }

    private void DiscoverAndSelect(string? path)
    {
        var deviceNode = CurrentDeviceNode;
        if (deviceNode is null)
            return;

        deviceNode.IsExpanded = true;

        if (string.IsNullOrEmpty(path)
            || Data.FileActions.IsDriveViewVisible
            || AdbLocation.LocationFromString(path) is Navigation.SpecialLocation.DriveView)
        {
            SelectTreeNode(deviceNode);
            return;
        }

        if (Data.FileActions.IsSearchMode
            || AdbLocation.LocationFromString(path) is Navigation.SpecialLocation.SearchMode)
        {
            return;
        }

        var drive = ResolveTreeDrive(path);
        if (drive is null)
        {
            SelectTreeNode(deviceNode);
            return;
        }

        var driveNode = deviceNode.Children.FirstOrDefault(n => n.Drive == drive);
        if (driveNode is null)
        {
            SelectTreeNode(deviceNode);
            return;
        }

        deviceNode.IsExpanded = true;

        NavigationTreeNode currentNode;
        if (drive.Type is AbstractDrive.DriveType.Package or AbstractDrive.DriveType.Trash
            || NavigationTreeNode.IsDriveRootPath(path, drive))
        {
            currentNode = driveNode;
        }
        else
        {
            var chain = BuildPathChain(path, drive);
            if (chain.Count == 0)
            {
                currentNode = driveNode;
            }
            else
            {
                currentNode = driveNode;
                driveNode.IsExpanded = true;

                for (var i = 0; i < chain.Count; i++)
                {
                    currentNode = FindOrCreateChild(currentNode, chain[i]);
                    if (i < chain.Count - 1)
                    {
                        currentNode.CanExpand = true;
                        currentNode.IsExpanded = true;
                    }
                }

                var location = Data.DirList?.CurrentLocation;
                if (location is not null && NavigationTreeNode.PathsEqual(location.FullPath, currentNode.Path))
                {
                    currentNode.DisplayName = location.DisplayName;
                    currentNode.Icon = location.Icon;
                }
            }
        }

        AddSubfolders(currentNode);
        currentNode.IsExpanded = true;
        SelectTreeNode(currentNode);
    }

    private void AddSubfolders(NavigationTreeNode node)
    {
        if (Data.FileActions.IsAppDrive || Data.FileActions.ListingInProgress)
            return;

        if (node.Drive?.Type is AbstractDrive.DriveType.Package or AbstractDrive.DriveType.Trash)
            return;

        ApplyTreeFolders(node, CurrentSubfolders().ToList());
        node.ChildrenLoaded = true;
        node.ChildrenLoading = false;
        _ = ProbeTreeCanExpandAsync(node);
    }

    private IEnumerable<FileClass> CurrentSubfolders()
    {
        var source = _currentExplorerItems();
        if (source is null)
            yield break;

        var allowHidden = Data.Settings.ShowHiddenItems;
        foreach (var item in source)
        {
            if (item is not FileClass file || !file.IsDirectory)
                continue;

            if (!allowHidden && file.IsHidden)
                continue;

            if (FileHelper.IsHiddenRecycleItem(file))
                continue;

            yield return file;
        }
    }

    private void OnTreeNodeExpanded(NavigationTreeNode node)
    {
        if (node.Device is not null)
            return;

        if (node.Drive?.Type is AbstractDrive.DriveType.Package or AbstractDrive.DriveType.Trash)
            return;

        _ = LoadTreeChildrenAsync(node);
    }

    private async Task LoadTreeChildrenAsync(NavigationTreeNode node)
    {
        if (node.ChildrenLoaded || node.ChildrenLoading)
            return;

        if (IsCurrentExplorerPath(node) && Data.FileActions.ListingInProgress)
            return;

        if (IsCurrentExplorerPath(node))
        {
            ApplyTreeFolders(node, CurrentSubfolders().ToList());
            node.ChildrenLoaded = true;
            await ProbeTreeCanExpandAsync(node);
            return;
        }

        var deviceId = node.OwnerDevice?.ID;
        if (string.IsNullOrEmpty(deviceId))
            return;

        node.ChildrenLoading = true;
        var path = node.Path;
        var token = Data.DeviceCts.Token;

        List<FileClass> folders;
        try
        {
            folders = await Task.Run(() => ListTreeSubfolders(deviceId, path, token), token);
        }
        catch (OperationCanceledException)
        {
            node.ChildrenLoading = false;
            return;
        }
        catch
        {
            node.ChildrenLoading = false;
            return;
        }

        App.SafeInvoke(() =>
        {
            if (!IsTreeNodeAttached(node))
            {
                node.ChildrenLoading = false;
                return;
            }

            ApplyTreeFolders(node, folders);
            node.ChildrenLoaded = true;
            node.ChildrenLoading = false;
        });

        await ProbeTreeCanExpandAsync(node);
    }

    private void ApplyTreeFolders(NavigationTreeNode node, List<FileClass> folders)
    {
        var matching = folders
            .Where(folder => node.IsDirectChildPath(folder.FullPath))
            .ToList();

        foreach (var folder in matching)
            FindOrCreateChild(node, folder.FullPath, folder);

        foreach (var child in node.Children.ToList())
        {
            if (child.Drive is not null)
                continue;

            if (matching.Any(folder => NavigationTreeNode.PathsEqual(folder.FullPath, child.Path)))
                continue;

            child.Detach();
            node.Children.Remove(child);
        }
    }

    private async Task ProbeTreeCanExpandAsync(NavigationTreeNode node)
    {
        var deviceId = node.OwnerDevice?.ID;
        if (string.IsNullOrEmpty(deviceId) || !IsTreeNodeAttached(node))
            return;

        var childPaths = node.Children
            .Where(child => child.Drive is null)
            .Select(child => child.Path)
            .ToList();

        if (childPaths.Count == 0)
        {
            if (!node.AlwaysExpandable)
                node.CanExpand = false;
            return;
        }

        var token = Data.DeviceCts.Token;
        var probePath = node.Path;
        if (node.Drive?.LinkTargetPath is string link && !string.IsNullOrEmpty(link))
            probePath = link;

        HashSet<string> withSubfolders;
        try
        {
            withSubfolders = await Task.Run(
                () => FoldersWithSubfolders(deviceId, node.Path, probePath, childPaths, token),
                token);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch
        {
            App.SafeInvoke(() =>
            {
                foreach (var child in node.Children.Where(c => c.Drive is null))
                    child.CanExpand = true;
            });
            return;
        }

        App.SafeInvoke(() =>
        {
            if (!IsTreeNodeAttached(node))
                return;

            foreach (var child in node.Children)
            {
                if (child.Drive is not null)
                    continue;

                child.CanExpand = withSubfolders.Any(path => NavigationTreeNode.PathsEqual(path, child.Path));
            }

            if (!node.AlwaysExpandable)
                node.CanExpand = node.Children.Count > 0;
        });
    }

    private static List<FileClass> ListTreeSubfolders(string deviceId, string path, CancellationToken token)
    {
        IEnumerable<FileStat> entries;
        try
        {
            if (ArchivePath.TryParse(path, out var archivePath, out var internalPath, deviceId))
                entries = ArchiveListing.ListEntries(deviceId, archivePath, internalPath, token);
            else
                entries = ADBService.ListDirectoryEntries(deviceId, path, token);
        }
        catch (ADBService.ProcessFailedException)
        {
            return [];
        }
        catch (OperationCanceledException)
        {
            return [];
        }

        var allowHidden = Data.Settings.ShowHiddenItems;
        var folders = new List<FileClass>();
        var unresolvedLinks = new List<FileStat>();
        foreach (var entry in entries)
        {
            if (!allowHidden && entry.FullName.StartsWith('.'))
                continue;

            if (entry.Type is AbstractFile.FileType.Folder)
            {
                var file = new FileClass(entry.FullName, entry.FullPath, AbstractFile.FileType.Folder, entry.IsLink);
                if (FileHelper.IsHiddenRecycleItem(file))
                    continue;

                folders.Add(file);
                continue;
            }

            if (entry.IsLink && entry.Type is AbstractFile.FileType.Unknown)
                unresolvedLinks.Add(entry);
        }

        if (unresolvedLinks.Count == 0)
            return folders;

        List<(string Target, AbstractFile.FileType Type)> linkTypes;
        try
        {
            var linkPaths = unresolvedLinks.Select(link => link.FullPath).ToList();
            linkTypes = [.. ADBService.GetLinkType(deviceId, linkPaths, token)];
        }
        catch (ADBService.ProcessFailedException)
        {
            return folders;
        }
        catch (OperationCanceledException)
        {
            return folders;
        }

        for (var i = 0; i < unresolvedLinks.Count && i < linkTypes.Count; i++)
        {
            if (linkTypes[i].Type is not AbstractFile.FileType.Folder)
                continue;

            var entry = unresolvedLinks[i];
            var file = new FileClass(entry.FullName, entry.FullPath, AbstractFile.FileType.Folder, isLink: true)
            {
                LinkTarget = linkTypes[i].Target
            };

            if (FileHelper.IsHiddenRecycleItem(file))
                continue;

            folders.Add(file);
        }

        return folders;
    }

    private static HashSet<string> FoldersWithSubfolders(
        string deviceId,
        string parentPath,
        string resolvedParentPath,
        List<string> childPaths,
        CancellationToken token)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        if (ArchivePath.IsArchivePath(parentPath, deviceId))
        {
            foreach (var childPath in childPaths)
            {
                if (HasTreeSubfolder(deviceId, childPath, token))
                    result.Add(childPath);
            }

            return result;
        }

        CollectFindSubfolderParents(deviceId, parentPath, token, result);
        if (result.Count == 0
            && !NavigationTreeNode.PathsEqual(parentPath, resolvedParentPath))
        {
            CollectFindSubfolderParents(deviceId, resolvedParentPath, token, result);
        }

        foreach (var childPath in childPaths)
        {
            if (result.Any(path => NavigationTreeNode.PathsEqual(path, childPath)))
                continue;

            if (HasTreeSubfolder(deviceId, childPath, token))
                result.Add(childPath);
        }

        return result;
    }

    private static void CollectFindSubfolderParents(
        string deviceId,
        string parentPath,
        CancellationToken token,
        HashSet<string> result)
    {
        var exit = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "find",
            out var stdout,
            out _,
            token,
            "-H",
            ADBService.EscapeAdbShellString(parentPath),
            "-mindepth",
            "2",
            "-maxdepth",
            "2",
            "-type",
            "d",
            "2>/dev/null");

        if (exit != 0 && string.IsNullOrWhiteSpace(stdout))
            return;

        var allowHidden = Data.Settings.ShowHiddenItems;
        foreach (var line in stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var grandchild = line.Trim();
            if (string.IsNullOrEmpty(grandchild) || grandchild.StartsWith("find:", StringComparison.Ordinal))
                continue;

            if (!allowHidden && FileHelper.GetFullName(grandchild).StartsWith('.'))
                continue;

            result.Add(FileHelper.GetParentPath(grandchild));
        }
    }

    private static bool HasTreeSubfolder(string deviceId, string path, CancellationToken token)
        => ListTreeSubfolders(deviceId, path, token).Count > 0;

    private void InvalidateTreeChildrenLoaded()
    {
        foreach (var node in TreeSource)
            InvalidateTreeChildrenLoaded(node);
    }

    private static void InvalidateTreeChildrenLoaded(NavigationTreeNode node)
    {
        if (node.Device is null
            && node.Drive?.Type is not AbstractDrive.DriveType.Package
            && node.Drive?.Type is not AbstractDrive.DriveType.Trash)
        {
            node.ChildrenLoaded = false;
            node.ChildrenLoading = false;
        }

        foreach (var child in node.Children)
            InvalidateTreeChildrenLoaded(child);
    }

    private void ReloadExpandedTreeFolders()
    {
        foreach (var node in TreeSource)
            ReloadExpandedTreeFolders(node);
    }

    private void ReloadExpandedTreeFolders(NavigationTreeNode node)
    {
        if (node.IsExpanded && node.Device is null)
            _ = LoadTreeChildrenAsync(node);

        foreach (var child in node.Children)
            ReloadExpandedTreeFolders(child);
    }

    private static bool IsCurrentExplorerPath(NavigationTreeNode node)
    {
        if (node.OwnerDevice is null || !node.OwnerDevice.IsOpen)
            return false;

        if (Data.FileActions.IsDriveViewVisible
            || Data.FileActions.IsSearchMode
            || Data.FileActions.IsAppDrive)
            return false;

        if (node.Drive is not null)
            return NavigationTreeNode.IsDriveRootPath(Data.CurrentPath, node.Drive);

        return NavigationTreeNode.PathsEqual(node.Path, Data.CurrentPath);
    }

    private bool IsTreeNodeAttached(NavigationTreeNode node)
        => TreeSource.Any(root => ReferenceEquals(root, node) || IsAncestor(node, root));

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

    public void UpdateCutStates()
    {
        foreach (var node in TreeSource)
            UpdateCutStates(node);
    }

    private static void UpdateCutStates(NavigationTreeNode node)
    {
        node.CutState = CutStateFor(node);
        foreach (var child in node.Children)
            UpdateCutStates(child);
    }

    private static DragDropEffects CutStateFor(NavigationTreeNode node)
    {
        if (node.Device is not null)
            return DragDropEffects.None;

        if (Data.CopyPaste.PasteSource is CopyPasteService.DataSource.None
            || Data.CopyPaste.Files.Length == 0)
            return DragDropEffects.None;

        if (!Data.CopyPaste.IsFromDevice(node.OwnerDevice))
            return DragDropEffects.None;

        if (!Data.CopyPaste.ContainsPath(node.Path))
            return DragDropEffects.None;

        return Data.CopyPaste.PasteState;
    }

    private NavigationTreeNode FindOrCreateChild(NavigationTreeNode parent, string path, FileClass? file = null)
    {
        var existing = parent.FindChild(path);
        if (existing is not null)
        {
            existing.UpdatePath(path);
            if (file is not null)
            {
                existing.DisplayName = file.DisplayName;
                existing.Icon = file.Icon;
                existing.IconOverlay = file.IconOverlay;
            }
            existing.CutState = CutStateFor(existing);
            return existing;
        }

        var icon = file?.Icon ?? NavigationTreeNode.FolderIcon(path);
        var child = new NavigationTreeNode(
            path,
            file?.DisplayName ?? NavigationTreeNode.FolderDisplayName(path),
            icon,
            OnTreeNodeSelected,
            ownerDevice: parent.OwnerDevice,
            onExpanded: OnTreeNodeExpanded)
        {
            IconOverlay = file?.IconOverlay
        };
        child.CutState = CutStateFor(child);

        InsertChild(parent, child);
        return child;
    }

    private static void InsertDrive(NavigationTreeNode deviceNode, NavigationTreeNode driveNode)
    {
        var index = 0;
        while (index < deviceNode.Children.Count && CompareDriveNodes(deviceNode.Children[index], driveNode) < 0)
            index++;

        deviceNode.Children.Insert(index, driveNode);
    }

    private static void InsertChild(NavigationTreeNode parent, NavigationTreeNode child)
    {
        var index = 0;
        while (index < parent.Children.Count
               && string.Compare(parent.Children[index].DisplayName, child.DisplayName, StringComparison.Ordinal) < 0)
        {
            index++;
        }

        parent.Children.Insert(index, child);
    }

    private static int CompareDriveNodes(NavigationTreeNode left, NavigationTreeNode right)
    {
        var leftType = (int)(left.Drive?.Type ?? AbstractDrive.DriveType.Unknown);
        var rightType = (int)(right.Drive?.Type ?? AbstractDrive.DriveType.Unknown);
        return leftType.CompareTo(rightType);
    }

    private void SelectTreeNode(NavigationTreeNode? node)
    {
        if (ReferenceEquals(_selectedTreeNode, node))
        {
            if (node is not null)
                node.SetSelected(true);
            return;
        }

        _selectedTreeNode?.SetSelected(false);
        _selectedTreeNode = node;
        _selectedTreeNode?.SetSelected(true);
    }

    private void OnTreeNodeSelected(NavigationTreeNode node)
    {
        if (_syncing)
            return;

        if (node.Device is { } device)
        {
            Data.RuntimeSettings.PendingLocationAfterDeviceOpen = null;

            if (!device.IsOpen)
                DeviceHelper.BrowseDeviceAction(device);
            else if (!Data.FileActions.IsDriveViewVisible)
                Data.RuntimeSettings.LocationToNavigate = new(Navigation.SpecialLocation.DriveView);
            return;
        }

        var owner = GetNodeDevice(node);
        if (owner is not null && !owner.IsOpen)
        {
            Data.RuntimeSettings.PendingLocationAfterDeviceOpen = new(node.Path);
            DeviceHelper.BrowseDeviceAction(owner);
            return;
        }

        if (IsTreeNodeCurrent(node))
            return;

        Data.RuntimeSettings.LocationToNavigate = new(node.Path);
    }

    private LogicalDeviceViewModel? GetNodeDevice(NavigationTreeNode node)
    {
        if (node.Device is not null)
            return node.Device;

        return TreeSource.FirstOrDefault(deviceNode => IsAncestor(node, deviceNode))?.Device;
    }

    private bool IsTreeNodeCurrent(NavigationTreeNode node)
    {
        if (node.Device is not null)
            return node.Device.IsOpen && Data.FileActions.IsDriveViewVisible;

        var owner = GetNodeDevice(node);
        if (owner is not null && !owner.IsOpen)
            return false;

        if (AdbLocation.LocationFromString(node.Path) is Navigation.SpecialLocation.DriveView)
            return Data.FileActions.IsDriveViewVisible;

        if (NavigationTreeNode.PathsEqual(node.Path, Data.CurrentPath))
            return true;

        if (node.Drive is null)
            return false;

        if (node.Drive.Type is AbstractDrive.DriveType.Trash && Data.FileActions.IsRecycleBin)
            return true;

        if (node.Drive.Type is AbstractDrive.DriveType.Package && Data.FileActions.IsAppDrive)
            return true;

        if (node.Drive.Type is AbstractDrive.DriveType.Temp && Data.FileActions.IsTemp)
            return true;

        return node.Drive == Data.CurrentDrive
            && NavigationTreeNode.IsDriveRootPath(Data.CurrentPath, node.Drive);
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

        return DriveHelper.GetCurrentDrive(path);
    }

    private static DriveViewModel? FindDrive(AbstractDrive.DriveType type)
        => Data.DevicesObject?.Current?.Drives.FirstOrDefault(d => d.Type == type);

    private static bool IsRecycleLocation(string path)
        => AdbExplorerConst.POSSIBLE_RECYCLE_PATHS.Any(recycle =>
            path == recycle || path.StartsWith($"{recycle}/", StringComparison.Ordinal));

    private static List<string> BuildPathChain(string path, DriveViewModel drive)
    {
        var chain = new List<string>();
        var current = path;

        while (!NavigationTreeNode.IsDriveRootPath(current, drive))
        {
            chain.Add(current);
            var parent = FileHelper.GetParentPath(current);
            if (parent == current)
                break;

            current = parent;
        }

        chain.Reverse();
        return chain;
    }

    private static IEnumerable<DriveViewModel> VisibleDrives(LogicalDeviceViewModel device)
    {
        return device.Drives.Where(drive => drive.Type switch
        {
            AbstractDrive.DriveType.Trash => Data.Settings.EnableRecycle,
            AbstractDrive.DriveType.Temp or AbstractDrive.DriveType.Package => Data.Settings.EnableApk,
            _ => true,
        });
    }

    private void ClearTree()
    {
        SelectTreeNode(null);
        foreach (var node in TreeSource)
            node.Detach();
        TreeSource.Clear();
    }

    private static bool IsAncestor(NavigationTreeNode? node, NavigationTreeNode ancestor)
    {
        if (node is null)
            return false;

        return ancestor.Children.Contains(node)
            || ancestor.Children.Any(child => IsAncestor(node, child));
    }
}
