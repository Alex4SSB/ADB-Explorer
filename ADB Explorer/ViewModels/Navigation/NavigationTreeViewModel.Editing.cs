namespace ADB_Explorer.ViewModels;

public partial class NavigationTreeViewModel
{
    private NavigationTreeNode? _queuedRename;

    private NavigationTreeNode? _queuedNewFolderParent;

    private NavigationTreeNode? _editingNode;

    public NavigationTreeNode? EditingNode => _editingNode;

    public event EventHandler<NavigationTreeNode>? NodeEditStarted;

    public void QueueRename(NavigationTreeNode node)
    {
        CancelEdit();
        _queuedNewFolderParent = null;
        _queuedRename = node;
    }

    public void QueueNewFolder(NavigationTreeNode parent)
    {
        CancelEdit();
        _queuedRename = null;
        _queuedNewFolderParent = parent;
    }

    public bool HasQueuedEdit => _queuedRename is not null || _queuedNewFolderParent is not null;

    public async void StartQueuedEdit()
    {
        var rename = _queuedRename;
        var parent = _queuedNewFolderParent;
        _queuedRename = null;
        _queuedNewFolderParent = null;

        if (rename is not null)
        {
            BeginEdit(rename);
            return;
        }

        if (parent is not null)
            await StartNewFolderAsync(parent);
    }

    private async Task StartNewFolderAsync(NavigationTreeNode parent)
    {
        parent.CanExpand = true;
        parent.IsExpanded = true;
        await LoadTreeChildrenAsync(parent);

        if (!IsTreeNodeAttached(parent))
            return;

        var siblingNames = parent.Children.Select(child => child.DisplayName);
        var fileName = FileHelper.DuplicateFile(siblingNames, ADB_Explorer.Strings.Resources.S_NEW_FOLDER);
        var path = FileHelper.ConcatPaths(parent.Path, fileName);
        var file = new FileClass(fileName, path, AbstractFile.FileType.Folder, isTemp: true);
        var child = new NavigationTreeNode(
            path,
            fileName,
            NavigationTreeNode.FolderIcon(path, parent.OwnerDevice?.ID),
            OnTreeNodeSelected,
            ownerDevice: parent.OwnerDevice,
            onExpanded: OnTreeNodeExpanded)
        {
            File = file,
            IsTemp = true,
            CanExpand = false,
        };
        InsertChild(parent, child);
        parent.CanExpand = true;
        BeginEdit(child);
    }

    private void BeginEdit(NavigationTreeNode node)
    {
        if (!ReferenceEquals(_editingNode, node))
            CancelEdit();

        node.File ??= new FileClass(node.DisplayName, node.Path, AbstractFile.FileType.Folder);
        _editingNode = node;
        node.IsInEditMode = true;
        Data.FileActions.IsExplorerEditing = true;
        NodeEditStarted?.Invoke(this, node);
    }

    public void CancelEdit(bool removeTemp = true)
    {
        if (_editingNode is null)
            return;

        var node = _editingNode;
        node.IsInEditMode = false;
        Data.FileActions.IsExplorerEditing = false;
        _editingNode = null;

        if (removeTemp && node.IsTemp)
            RemoveTempNode(node);

        BlockReselectAfterEdit(node);
    }

    private void RemoveTempNode(NavigationTreeNode node)
    {
        var parent = FindParent(node);
        if (parent is null)
            return;

        node.Detach();
        parent.Children.Remove(node);
        RestoreParentChevron(parent);
    }

    private static void RestoreParentChevron(NavigationTreeNode parent)
    {
        if (parent.AlwaysExpandable)
            return;

        if (parent.Children.Any(child => child.Drive is null))
            return;

        parent.CanExpand = false;
        parent.IsExpanded = false;
    }

    public void RemoveDeletedFolder(string deviceId, string path)
    {
        App.SafeInvoke(() =>
        {
            var node = FindNode(TreeSource, candidate =>
                candidate.Device is null
                && candidate.Drive is null
                && candidate.OwnerDevice?.ID == deviceId
                && NavigationTreeNode.PathsEqual(candidate.Path, path));

            if (node is null)
                return;

            var parent = FindParent(node);
            var parentPath = parent?.Path;
            if (parent is not null)
            {
                node.Detach();
                parent.Children.Remove(node);
                RestoreParentChevron(parent);
            }

            if (deviceId != ActiveDevice?.ID || string.IsNullOrEmpty(Data.CurrentPath))
                return;

            var current = NavigationTreeNode.NormalizePath(Data.CurrentPath);
            var deleted = NavigationTreeNode.NormalizePath(path);
            var isCurrentOrChild = NavigationTreeNode.PathsEqual(current, deleted)
                || current.StartsWith($"{deleted}/", StringComparison.Ordinal);

            if (isCurrentOrChild && !string.IsNullOrEmpty(parentPath))
                Data.RequestNavigation(new(parentPath));
        });
    }

    public void RenameFolder(string deviceId, string oldPath, string newPath)
    {
        App.SafeInvoke(() =>
        {
            var node = FindNode(TreeSource, candidate =>
                candidate.Device is null
                && candidate.Drive is null
                && candidate.OwnerDevice?.ID == deviceId
                && (NavigationTreeNode.PathsEqual(candidate.Path, oldPath)
                    || NavigationTreeNode.PathsEqual(candidate.Path, newPath)));

            if (node is not null)
            {
                node.DisplayName = NavigationTreeNode.FolderDisplayName(newPath, deviceId);
                UpdateSubtreePaths(node, oldPath, newPath);
            }

            if (deviceId != ActiveDevice?.ID || string.IsNullOrEmpty(Data.CurrentPath))
                return;

            var current = NavigationTreeNode.NormalizePath(Data.CurrentPath);
            var oldNorm = NavigationTreeNode.NormalizePath(oldPath);
            if (NavigationTreeNode.PathsEqual(current, oldNorm))
            {
                Data.RequestNavigation(new(newPath));
                return;
            }

            if (!current.StartsWith($"{oldNorm}/", StringComparison.Ordinal))
                return;

            Data.RequestNavigation(new(newPath + current[oldNorm.Length..]));
        });
    }

    /// <summary>
    /// Adds a node for a folder that just finished being created on <paramref name="deviceId"/> — via a push
    /// from Windows, or a device-internal move/copy — when its parent is already in the tree. If the parent
    /// is expanded or its children were previously loaded, the new folder appears immediately; otherwise the
    /// chevron is enabled so a later expand lists it from the device.
    /// </summary>
    public void AddCreatedFolder(string deviceId, string path)
    {
        App.SafeInvoke(() =>
        {
            var parent = FindNode(TreeSource, candidate =>
                candidate.OwnerDevice?.ID == deviceId
                && candidate.Device is null
                && candidate.IsDirectChildPath(path));

            if (parent is null)
                return;

            if (parent.ChildrenLoaded || parent.IsExpanded)
                FindOrCreateChild(parent, path);

            parent.CanExpand = true;
        });
    }

    private static void UpdateSubtreePaths(NavigationTreeNode node, string oldPath, string newPath)
    {
        node.UpdatePath(RewritePath(node.Path, oldPath, newPath, node.OwnerDevice?.ID));
        if (node.File is not null)
            node.File.UpdatePath(node.Path);

        foreach (var child in node.Children)
            UpdateSubtreePaths(child, oldPath, newPath);
    }

    private static string RewritePath(string path, string oldPath, string newPath, string? deviceId = null)
    {
        var current = NavigationTreeNode.NormalizePath(path, deviceId);
        var oldNorm = NavigationTreeNode.NormalizePath(oldPath, deviceId);
        if (NavigationTreeNode.PathsEqual(current, oldNorm))
            return newPath;

        if (current.StartsWith($"{oldNorm}/", StringComparison.Ordinal))
            return newPath + current[oldNorm.Length..];

        return path;
    }

    public void CancelTempFile(FileClass file)
    {
        var node = FindNodeByFile(file);
        if (node is null)
            return;

        if (ReferenceEquals(_editingNode, node))
            CancelEdit(removeTemp: true);
        else if (node.IsTemp)
            RemoveTempNode(node);
    }

    public void CompleteTempFile(FileClass file)
    {
        var node = FindNodeByFile(file);
        if (node is null)
            return;

        node.IsTemp = false;
        node.UpdatePath(file.FullPath);
        node.DisplayName = file.DisplayName;
        node.File = file;

        var parent = FindParent(node);
        if (parent is not null)
            parent.CanExpand = true;
    }

    public void CommitEdit(string text, bool restorePreviousSelection = true)
    {
        if (_editingNode is null)
            return;

        var node = _editingNode;
        FileActionLogic.RenameTreeNode(node, text);
        node.IsInEditMode = false;
        Data.FileActions.IsExplorerEditing = false;
        _editingNode = null;

        if (restorePreviousSelection)
            BlockReselectAfterEdit(node);
    }

    public void EscapeEdit()
    {
        if (_editingNode is not null)
            CancelEdit(removeTemp: _editingNode.IsTemp);
    }

    public static DriveRestrictions? GetRenameRestrictions(NavigationTreeNode node)
    {
        var drive = node.Drive
            ?? DriveHelper.GetCurrentDrive(node.Path, node.OwnerDevice)
            ?? Data.CurrentDrive;

        return drive?.Restrictions;
    }

    public void UpdateRenameLegality(NavigationTreeNode node, string text, DriveRestrictions restrictions)
    {
        node.IsRenameUnixLegal = FileHelper.FileNameLegal(text, FileHelper.RenameTarget.Unix);
        node.IsRenameNamingLegal = FileHelper.FileNameLegal(text, FileHelper.RenameTarget.RestrictedNaming);
        node.IsRenameWindowsLegal = FileHelper.FileNameLegal(text, FileHelper.RenameTarget.Windows);
        node.IsRenameDriveRootLegal = FileHelper.FileNameLegal(text, FileHelper.RenameTarget.WinRoot);

        var comparison = restrictions.CaseInsensitiveNames
            ? StringComparison.InvariantCultureIgnoreCase
            : StringComparison.InvariantCulture;

        var parent = FindParent(node);
        var siblings = parent?.Children ?? [];
        node.IsRenameUnique = !siblings.Any(child =>
            !ReferenceEquals(child, node)
            && child.DisplayName.Equals(text, comparison));
    }

    private void BlockReselectAfterEdit(NavigationTreeNode node)
    {
        NavigationTreeNode.SuppressUserSelectFromEdit++;
        node.SetSelected(false);
        _selectedTreeNode?.SetSelected(true);

        App.SafeBeginInvoke(() =>
        {
            node.SetSelected(false);
            _selectedTreeNode?.SetSelected(true);
            if (NavigationTreeNode.SuppressUserSelectFromEdit > 0)
                NavigationTreeNode.SuppressUserSelectFromEdit--;
        }, DispatcherPriority.ContextIdle);
    }
}
