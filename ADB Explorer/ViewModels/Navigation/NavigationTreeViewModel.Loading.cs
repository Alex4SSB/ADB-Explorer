namespace ADB_Explorer.ViewModels;

public partial class NavigationTreeViewModel
{
    private readonly Dictionary<NavigationTreeNode, Task> _childLoadTasks = [];

    private void AddSubfolders(NavigationTreeNode node)
    {
        if (Data.FileActions.IsAppDrive || Data.FileActions.ListingInProgress)
            return;

        if (node.Drive?.Type is AbstractDrive.DriveType.Package or AbstractDrive.DriveType.Trash)
            return;

        ApplyTreeFolders(node, CurrentSubfolders().ToList());
        node.ChildrenLoaded = true;
        node.ChildrenLoading = false;
        UpdateCanExpand(node);
    }

    private IEnumerable<FileClass> CurrentSubfolders()
    {
        var source = _currentExplorerItems();
        if (source is null)
            return [];

        return SubfoldersOf(source, ActiveDevice?.ID, Data.FileActions.IsArchive);
    }

    /// <summary>The items of a folder's listing that can be navigated into: folders and navigable archives.</summary>
    internal static IEnumerable<FileClass> SubfoldersOf(IEnumerable<IBrowserItem> source, string? deviceId, bool isInsideArchive)
    {
        var allowHidden = Data.Settings.ShowHiddenItems;
        foreach (var item in source)
        {
            if (item is not FileClass file)
                continue;

            if (!allowHidden && file.IsHidden)
                continue;

            if (FileHelper.IsHiddenRecycleItem(file))
                continue;

            if (file.IsDirectory)
            {
                yield return file;
                continue;
            }

            if (deviceId is not null
                && ArchiveHelper.CanNavigateIntoArchive(file.FullPath, file.FullName, deviceId, isInsideArchive))
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

    private Task LoadTreeChildrenAsync(NavigationTreeNode node)
    {
        if (node.ChildrenLoaded)
            return Task.CompletedTask;

        if (_childLoadTasks.TryGetValue(node, out var inflight))
            return inflight;

        var task = LoadTreeChildrenCoreAsync(node);
        _childLoadTasks[node] = task;
        _ = task.ContinueWith(_ => App.SafeInvoke(() =>
        {
            if (_childLoadTasks.TryGetValue(node, out var current) && ReferenceEquals(current, task))
                _childLoadTasks.Remove(node);
        }));
        return task;
    }

    private async Task LoadTreeChildrenCoreAsync(NavigationTreeNode node)
    {
        var epoch = node.ChildrenLoadEpoch;

        if (IsCurrentExplorerPath(node) && Data.FileActions.ListingInProgress)
            return;

        if (IsCurrentExplorerPath(node))
        {
            ApplyTreeFolders(node, CurrentSubfolders().ToList());
            node.ChildrenLoaded = true;
            UpdateCanExpand(node);
            return;
        }

        var deviceId = node.OwnerDevice?.ID;
        if (string.IsNullOrEmpty(deviceId))
            return;

        node.ChildrenLoading = true;
        var path = node.DropTargetPath;
        var token = TreeListingToken(node);

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
            if (epoch != node.ChildrenLoadEpoch
                || token.IsCancellationRequested
                || !IsTreeNodeAttached(node))
            {
                node.ChildrenLoading = false;
                return;
            }

            ApplyTreeFolders(node, folders);
            node.ChildrenLoaded = true;
            node.ChildrenLoading = false;
            UpdateCanExpand(node);
        });
    }

    private void ApplyTreeFolders(NavigationTreeNode node, List<FileClass> folders)
    {
        var deviceId = node.OwnerDevice?.ID;
        var matching = folders
            .Select(folder => (File: folder, Path: TreeItemPath(folder, deviceId)))
            .Where(item => node.IsDirectChildPath(item.Path) || node.IsDirectChildPath(item.File.FullPath))
            .ToList();

        foreach (var item in matching)
            FindOrCreateChild(node, item.Path, item.File);

        foreach (var child in node.Children.ToList())
        {
            if (child.Drive is not null || child.IsTemp || child.IsInEditMode)
                continue;

            if (matching.Any(item => NavigationTreeNode.PathsEqual(item.Path, child.Path)))
                continue;

            child.Detach();
            node.Children.Remove(child);
        }
    }

    /// <summary>Folders show a chevron until expanding them finds no subfolders.</summary>
    private static void UpdateCanExpand(NavigationTreeNode node)
    {
        if (!node.AlwaysExpandable)
            node.CanExpand = node.Children.Count > 0;
    }

    internal static List<FileClass> ListTreeSubfolders(string deviceId, string path, CancellationToken token)
    {
        List<FileStat> entries;
        var listingInsideArchive = ArchivePath.TryParse(path, out var archivePath, out var internalPath, deviceId);
        try
        {
            // Materialize here - the listing is lazy, so failures would otherwise escape this catch
            if (listingInsideArchive)
                entries = [.. ArchiveListing.ListEntries(deviceId, archivePath, internalPath, token)];
            else
                entries = [.. AdbService.ListDirectoryEntries(deviceId, path, token)];
        }
        catch (AdbService.ProcessFailedException)
        {
            return [];
        }

        var allowHidden = Data.Settings.ShowHiddenItems;
        var device = Data.DevicesObject?.LogicalDeviceViewModels.FirstOrDefault(d => d.Matches(deviceId));
        var folders = new List<FileClass>();
        var unresolvedLinks = new List<FileStat>();
        foreach (var entry in entries)
        {
            if (!allowHidden && entry.FullName.StartsWith('.'))
                continue;

            if (entry.Type is AbstractFile.FileType.Folder)
            {
                var file = new FileClass(entry.FullName, entry.FullPath, AbstractFile.FileType.Folder, entry.IsLink) { Device = device };
                if (FileHelper.IsHiddenRecycleItem(file))
                    continue;

                folders.Add(file);
                continue;
            }

            if (!listingInsideArchive
                && entry.Type is AbstractFile.FileType.File
                && !entry.IsLink
                && ArchiveHelper.IsNavigableArchive(entry.FullName, deviceId))
            {
                var archive = new FileClass(entry.FullName, entry.FullPath, AbstractFile.FileType.File) { Device = device };
                if (FileHelper.IsHiddenRecycleItem(archive))
                    continue;

                folders.Add(archive);
                continue;
            }

            if (entry.IsLink && entry.Type is AbstractFile.FileType.Unknown)
                unresolvedLinks.Add(entry);
        }

        if (listingInsideArchive || unresolvedLinks.Count == 0)
            return folders;

        List<(string Target, AbstractFile.FileType Type)> linkTypes;
        try
        {
            var linkPaths = unresolvedLinks.Select(link => link.FullPath).ToList();
            linkTypes = [.. AdbService.GetLinkType(deviceId, linkPaths, token)];
        }
        catch (AdbService.ProcessFailedException)
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
                LinkTarget = linkTypes[i].Target,
                Device = device,
            };

            if (FileHelper.IsHiddenRecycleItem(file))
                continue;

            folders.Add(file);
        }

        return folders;
    }

    private void InvalidateTreeChildrenLoaded()
    {
        _childLoadTasks.Clear();
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
            node.ChildrenLoadEpoch++;
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

    /// <summary>
    /// Token for ADB listing of a tree node. <see cref="Data.DeviceCts"/> is cancelled whenever the
    /// explorer's current device is cleared (including while no device is open, on every poll) — so
    /// listings for other devices, or any device while none is selected, must not use it.
    /// </summary>
    private static CancellationToken TreeListingToken(NavigationTreeNode node)
    {
        if (IsActiveDevice(node.OwnerDevice))
            return Data.DeviceCts.Token;

        return CancellationToken.None;
    }

    private readonly HashSet<FileOperation> _watchedFileOps = [];

    /// <summary>Watches file ops, so a tree folder that just received items (drop / paste) shows them when next expanded.</summary>
    public void SubscribeFileOps(FileOperationQueue queue)
    {
        queue.Operations.CollectionChanged += (_, _) => App.SafeInvoke(() => WatchFileOps(queue));
        WatchFileOps(queue);
    }

    // Batch adds raise Reset, which carries no items - diff against the ops already watched
    private void WatchFileOps(FileOperationQueue queue)
    {
        _watchedFileOps.IntersectWith(queue.Operations);

        foreach (var op in queue.Operations.Where(_watchedFileOps.Add).ToList())
            op.WhenCompleted(() => App.SafeInvoke(() => OnFileOpCompleted(op)));
    }

    private void OnFileOpCompleted(FileOperation op)
    {
        if (op.TargetPath is not { PathType: AbstractFile.FilePathType.Android })
            return;

        var deviceId = (op.TargetDevice ?? op.Device).ID;
        var folder = op.TargetFolderPath;
        var node = FindNode(TreeSource, n => n.Device is null
                                          && n.Drive is null
                                          && n.OwnerDevice?.ID == deviceId
                                          && NavigationTreeNode.PathsEqual(n.DropTargetPath, folder));

        // The current folder follows its own listing
        if (node is null || IsCurrentExplorerPath(node))
            return;

        // Lazy again: its children are reloaded on the next expand, and an empty folder regains its chevron
        node.CanExpand = true;
        node.ChildrenLoaded = false;
        node.ChildrenLoadEpoch++;

        if (node.IsExpanded)
            _ = LoadTreeChildrenAsync(node);
    }
}
