namespace ADB_Explorer.ViewModels;

public partial class NavigationTreeViewModel : ObservableObject
{
    private readonly Func<IEnumerable<IBrowserItem>?> _currentExplorerItems;
    private readonly HashSet<ObservableList<DriveViewModel>> _subscribedDriveLists = [];
    private NavigationTreeNode? _selectedTreeNode;
    private bool _syncing;

    [ObservableProperty]
    public partial ObservableList<NavigationTreeNode> TreeSource { get; set; } = [];

    public NavigationTreeNode? ContextTarget { get; private set; }

    public bool IsTreeDragBlocked
        => _editingNode is not null
        || HasQueuedEdit
        || FindNode(TreeSource, node => node.IsTemp) is not null;

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

                // While the tab shows a page, nothing in the tree is the current location.
                if (Data.ActiveExplorerInstance.IsShowingPage)
                    SelectTreeNode(null);
                else if (!IsTreeDragBlocked)
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
                        if (!IsArchiveRootNode(currentNode))
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

    private static bool IsCurrentExplorerPath(NavigationTreeNode node)
    {
        if (!IsActiveDevice(node.OwnerDevice))
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
                existing.File ??= file;
            }
            existing.CutState = CutStateFor(existing);
            RefreshArchiveCanExpand(existing);
            return existing;
        }

        var deviceId = parent.OwnerDevice?.ID;
        var icon = file?.Icon ?? NavigationTreeNode.FolderIcon(path, deviceId);
        var child = new NavigationTreeNode(
            path,
            file?.DisplayName ?? NavigationTreeNode.FolderDisplayName(path, deviceId),
            icon,
            OnTreeNodeSelected,
            ownerDevice: parent.OwnerDevice,
            onExpanded: OnTreeNodeExpanded)
        {
            IconOverlay = file?.IconOverlay,
            File = file
        };
        child.CutState = CutStateFor(child);
        RefreshArchiveCanExpand(child);

        InsertChild(parent, child);
        return child;
    }

    private static string TreeItemPath(FileClass file, string? deviceId)
    {
        if (deviceId is not null
            && ArchiveHelper.CanNavigateIntoArchive(
                file.FullPath,
                file.FullName,
                deviceId,
                ArchivePath.IsArchivePath(file.FullPath, deviceId)))
            return ArchivePath.Join(file.FullPath, "");

        return file.FullPath;
    }

    private static bool IsArchiveRootNode(NavigationTreeNode node)
    {
        var deviceId = node.OwnerDevice?.ID;
        return deviceId is not null
            && ArchivePath.TryParse(node.Path, out _, out var internalPath, deviceId)
            && string.IsNullOrEmpty(internalPath);
    }

    private static void RefreshArchiveCanExpand(NavigationTreeNode node)
    {
        var deviceId = node.OwnerDevice?.ID;
        if (string.IsNullOrEmpty(deviceId)
            || !ArchivePath.TryParse(node.Path, out var archivePath, out var internalPath, deviceId)
            || !string.IsNullOrEmpty(internalPath))
            return;

        node.CanExpand = ArchiveListing.HasCachedToc(archivePath);
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
        if (_syncing || node.IsInEditMode || node.IsTemp || _editingNode is not null || DeviceHelper.IsSwitchingTabDevice)
            return;

        if (node.Device is { } device)
        {
            Data.RuntimeSettings.PendingLocationAfterDeviceOpen = null;

            if (!IsActiveDevice(device))
                DeviceHelper.SwitchTabToDevice(device);
            else if (!Data.FileActions.IsDriveViewVisible || Data.ActiveExplorerInstance.IsShowingPage)
                Data.RequestNavigation(new(Navigation.SpecialLocation.DriveView));
            return;
        }

        var owner = GetNodeDevice(node);
        if (owner is not null && !IsActiveDevice(owner))
        {
            Data.RuntimeSettings.PendingLocationAfterDeviceOpen = new(node.Path);
            DeviceHelper.SwitchTabToDevice(owner);
            return;
        }

        if (IsTreeNodeCurrent(node))
            return;

        Data.RequestNavigation(new(node.Path));
    }

    /// <summary>Opens a device (its drive view), drive or folder node in a new tab.</summary>
    public void OpenNodeInNewTab(NavigationTreeNode node)
    {
        if (node.IsTemp || node.IsInEditMode || GetNodeDevice(node) is not { } device)
            return;

        AdbLocation? location = null;
        if (node.Device is null && !string.IsNullOrEmpty(node.Path))
            location = new(node.Path);

        DeviceHelper.OpenDeviceInNewTab(device, location);
    }

    private LogicalDeviceViewModel? GetNodeDevice(NavigationTreeNode node)
    {
        if (node.Device is not null)
            return node.Device;

        return TreeSource.FirstOrDefault(deviceNode => IsAncestor(node, deviceNode))?.Device;
    }

    private bool IsTreeNodeCurrent(NavigationTreeNode node)
    {
        if (Data.ActiveExplorerInstance.IsShowingPage)
            return false;

        if (node.Device is not null)
            return IsActiveDevice(node.Device) && Data.FileActions.IsDriveViewVisible;

        var owner = GetNodeDevice(node);
        if (owner is not null && !IsActiveDevice(owner))
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

    public void SetContextTarget(NavigationTreeNode? node)
        => ContextTarget = node;

    private NavigationTreeNode? FindParent(NavigationTreeNode child)
        => FindParent(TreeSource, child);

    private static NavigationTreeNode? FindParent(IEnumerable<NavigationTreeNode> nodes, NavigationTreeNode child)
    {
        foreach (var node in nodes)
        {
            if (node.Children.Contains(child))
                return node;

            var nested = FindParent(node.Children, child);
            if (nested is not null)
                return nested;
        }

        return null;
    }

    private NavigationTreeNode? FindNodeByFile(FileClass file)
        => FindNode(TreeSource, node => ReferenceEquals(node.File, file));

    private static NavigationTreeNode? FindNode(IEnumerable<NavigationTreeNode> nodes, Func<NavigationTreeNode, bool> match)
    {
        foreach (var node in nodes)
        {
            if (match(node))
                return node;

            var nested = FindNode(node.Children, match);
            if (nested is not null)
                return nested;
        }

        return null;
    }
}
