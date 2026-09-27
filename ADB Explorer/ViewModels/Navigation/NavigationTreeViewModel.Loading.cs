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
        _ = ProbeTreeCanExpandAsync(node);
    }

    private IEnumerable<FileClass> CurrentSubfolders()
    {
        var source = _currentExplorerItems();
        if (source is null)
            yield break;

        var allowHidden = Data.Settings.ShowHiddenItems;
        var isInsideArchive = Data.FileActions.IsArchive;
        var deviceId = ActiveDevice?.ID;
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
            await ProbeTreeCanExpandAsync(node);
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

        var applied = false;
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
            applied = true;
        });

        if (applied)
            await ProbeTreeCanExpandAsync(node);
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

    private async Task ProbeTreeCanExpandAsync(NavigationTreeNode node)
    {
        var deviceId = node.OwnerDevice?.ID;
        if (string.IsNullOrEmpty(deviceId) || !IsTreeNodeAttached(node))
            return;

        var epoch = node.ChildrenLoadEpoch;

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

        var token = TreeListingToken(node);
        var probePath = node.DropTargetPath;

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
                if (epoch != node.ChildrenLoadEpoch)
                    return;

                foreach (var child in node.Children.Where(c => c.Drive is null))
                {
                    if (IsArchiveRootNode(child))
                        RefreshArchiveCanExpand(child);
                    else
                        child.CanExpand = true;
                }
            });
            return;
        }

        App.SafeInvoke(() =>
        {
            if (epoch != node.ChildrenLoadEpoch || !IsTreeNodeAttached(node))
                return;

            foreach (var child in node.Children)
            {
                if (child.Drive is not null)
                    continue;

                if (IsArchiveRootNode(child))
                    RefreshArchiveCanExpand(child);
                else
                    child.CanExpand = withSubfolders.Any(path => NavigationTreeNode.PathsEqual(path, child.Path));
            }

            if (!node.AlwaysExpandable)
                node.CanExpand = node.Children.Count > 0;
        });
    }

    private static List<FileClass> ListTreeSubfolders(string deviceId, string path, CancellationToken token)
    {
        IEnumerable<FileStat> entries;
        var listingInsideArchive = ArchivePath.TryParse(path, out var archivePath, out var internalPath, deviceId);
        try
        {
            if (listingInsideArchive)
                entries = ArchiveListing.TryListCachedEntries(archivePath, internalPath);
            else
                entries = AdbService.ListDirectoryEntries(deviceId, path, token);
        }
        catch (AdbService.ProcessFailedException)
        {
            return [];
        }

        var allowHidden = Data.Settings.ShowHiddenItems;
        var device = Data.DevicesObject?.LogicalDeviceViewModels.FirstOrDefault(d => d.ID == deviceId);
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
        var exit = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "find",
            out var stdout,
            out _,
            token,
            "-H",
            AdbService.EscapeAdbShellString(parentPath),
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
        foreach (var line in stdout.Split(AdbService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
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
}
