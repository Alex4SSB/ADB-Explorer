using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

public partial class CopyPasteService
{
    public static async void VerifyAndPush(string targetPath, IEnumerable<ShellItem> pasteItems, LogicalDeviceViewModel? device = null)
    {
        device ??= Data.ActiveDevice;
        if (device is null)
            return;

        var deviceId = device.ID;
        var skipMergeForForeignArchive = ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId)
            && !IsExplorerListing(targetPath, device);

        IEnumerable<string> files;
        IReadOnlySet<string> replacePaths = EmptyPathSet;
        IReadOnlySet<string> conflictPaths = EmptyPathSet;
        if (skipMergeForForeignArchive)
            files = pasteItems.Select(f => f.ParsingName);
        else
        {
            var outcome = await MergeFiles(pasteItems.Select(f => f.ParsingName), targetPath, device);
            files = outcome.Items;
            replacePaths = outcome.ReplaceRelativePaths;
            conflictPaths = outcome.ConflictRelativePaths;
            if (!files.Any())
                return;
        }

        if (files.Count() < pasteItems.Count())
            pasteItems = pasteItems.Where(f => files.Contains(f.ParsingName));

        if (ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
        {
            ShellFileOperation.PushItemsToTar(device, pasteItems, targetPath, App.AppDispatcher);
            return;
        }

        FileActionLogic.PushShellObjects(pasteItems, targetPath, replacePaths: replacePaths, conflictPaths: conflictPaths, device: device);
    }

    public static async void VerifyAndPush(string targetPath, IEnumerable<FileClass> pasteItems, DragDropEffects dropEffects = DragDropEffects.Copy, LogicalDeviceViewModel? device = null)
        => await VerifyAndPushAsync(targetPath, pasteItems, dropEffects, device);

    /// <summary>Completes once the pushes are queued, or nothing is to be pushed.</summary>
    public static async Task VerifyAndPushAsync(string targetPath, IEnumerable<FileClass> pasteItems, DragDropEffects dropEffects = DragDropEffects.Copy, LogicalDeviceViewModel? device = null)
    {
        device ??= Data.ActiveDevice;
        if (device is null)
            return;

        var deviceId = device.ID;
        var skipMergeForForeignArchive = ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId)
            && !IsExplorerListing(targetPath, device);

        IReadOnlySet<string> replacePaths = EmptyPathSet;
        IReadOnlySet<string> conflictPaths = EmptyPathSet;
        if (!skipMergeForForeignArchive)
        {
            var outcome = await MergeFiles(targetPath, pasteItems, device);
            pasteItems = outcome.Items;
            replacePaths = outcome.ReplaceRelativePaths;
            conflictPaths = outcome.ConflictRelativePaths;
            if (outcome.Items.Count == 0)
                return;
        }

        if (ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
        {
            ShellFileOperation.PushItemsToTar(
                device,
                pasteItems.Select(f => f.ShellItem).OfType<ShellItem>(),
                targetPath,
                App.AppDispatcher);
            return;
        }

        FileActionLogic.PushShellObjects(
            pasteItems.Select(f => f.ShellItem).OfType<ShellItem>(),
            targetPath,
            dropEffects,
            replacePaths,
            conflictPaths,
            device);
    }

    public static FileSyncOperation? VerifyAndPush(string targetPath, FileClass pasteItem, DragDropEffects dropEffects = DragDropEffects.Copy, ShellItem? originalShellItem = null, LogicalDeviceViewModel? device = null)
    {
        device ??= Data.ActiveDevice;
        if (device is null)
            return null;

        var deviceId = device.ID;
        var skipMergeForForeignArchive = ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId)
            && !IsExplorerListing(targetPath, device);

        IReadOnlySet<string> replacePaths = EmptyPathSet;
        IReadOnlySet<string> conflictPaths = EmptyPathSet;
        if (!skipMergeForForeignArchive)
        {
            var outcome = MergeFiles(targetPath, (IEnumerable<FileClass>)[pasteItem], device).Result;
            var items = outcome.Items;
            replacePaths = outcome.ReplaceRelativePaths;
            conflictPaths = outcome.ConflictRelativePaths;
            if (items.Count == 0)
                return null;

            pasteItem = items[0];
        }

        if (ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
        {
            ShellFileOperation.PushItemsToTar(
                device,
                [pasteItem.ShellItem ?? originalShellItem ?? ShellItem.Open(pasteItem.FullPath)],
                targetPath,
                App.AppDispatcher);

            return null;
        }

        return FileActionLogic.PushShellObject(
            pasteItem.ShellItem ?? originalShellItem ?? ShellItem.Open(pasteItem.FullPath),
            targetPath,
            dropEffects,
            originalShellItem,
            replacePaths,
            conflictPaths,
            device);
    }

    /// <summary>
    /// Resolves conflicts at the target, then pushes the files a FileGroupDescriptor describes,
    /// each read from the data object's FileContents as it's pushed.
    /// </summary>
    public static async void VerifyAndPushContents(
        string targetPath,
        System.Runtime.InteropServices.ComTypes.IDataObject dataObject,
        FileDescriptor[] descriptors,
        LogicalDeviceViewModel? device)
    {
        device ??= Data.ActiveDevice;
        if (device is null)
            return;

        var roots = GetContentTrees(dataObject, descriptors);
        if (roots.Count == 0)
            return;

        // Conflicts inside a folder are found by listing its tree, which only exists here
        roots.ForEach(SyncFile.RegisterVirtualRoot);

        try
        {
            var candidates = roots.Select(root => new FileClass(
                root.FullName,
                root.FullPath,
                root.IsDirectory ? FileType.Folder : FileType.File,
                size: root.Size,
                modifiedTime: root.SourceModified) { PathType = FilePathType.Windows });

            var outcome = await MergeFiles(targetPath, candidates, device);
            if (outcome.Items.Count == 0)
                return;

            var kept = outcome.Items.Select(f => f.FullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            FileActionLogic.PushVirtualItems(
                [.. roots.Where(root => kept.Contains(root.FullPath))],
                targetPath,
                outcome.ReplaceRelativePaths,
                outcome.ConflictRelativePaths,
                device);
        }
        finally
        {
            roots.ForEach(SyncFile.UnregisterVirtualRoot);
        }
    }

    /// <summary>
    /// The described files as trees, their content opened from the data object when pushed. Their paths are
    /// under the temp folder only to have a Windows shape; nothing is written there.
    /// </summary>
    internal static List<SyncFile> GetContentTrees(System.Runtime.InteropServices.ComTypes.IDataObject dataObject, FileDescriptor[] descriptors)
    {
        // A folder of their own, so trees of two pastes with the same names are told apart while registered
        var root = FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath.TrimEnd('\\'), Guid.NewGuid().ToString("N"), '\\');
        Dictionary<string, SyncFile> nodes = new(StringComparer.OrdinalIgnoreCase);

        for (var i = 0; i < descriptors.Length; i++)
        {
            var descriptor = descriptors[i];
            if (FileHelper.HasParentReference(descriptor.Name))
                continue;

            var index = i;
            var path = FileHelper.ConcatPaths(root, descriptor.Name, '\\');

            // A descriptor without FD_WRITESTIME reads as 1601
            DateTime? modified = descriptor.ChangeTimeUtc is { Year: > 1601 } time ? time : null;

            Func<Stream>? openContent = null;
            if (!descriptor.IsDirectory)
                openContent = () => ShellContentStream.FromDataObject(dataObject, index);

            nodes[path] = new(path, descriptor.IsDirectory, descriptor.Length, modified, openContent);
        }

        // Linked once all exist, as a folder may be described after its files, or not at all
        List<SyncFile> roots = [];
        foreach (var node in nodes.Values.ToList())
        {
            var child = node;
            var parentPath = FileHelper.GetParentPath(child.FullPath);
            while (parentPath.Length > root.Length)
            {
                if (nodes.TryGetValue(parentPath, out var parent))
                {
                    parent.Children.Add(child);
                    child = null;
                    break;
                }

                parent = new SyncFile(parentPath, true, null, null, null);
                nodes[parentPath] = parent;
                parent.Children.Add(child);
                child = parent;
                parentPath = FileHelper.GetParentPath(parentPath);
            }

            if (child is not null)
                roots.Add(child);
        }

        return roots;
    }

    /// <summary>Resolves conflicts at the target, then queues a device to device transfer.</summary>
    public static async void VerifyAndTransfer(
        string targetPath,
        IEnumerable<FileClass> transferItems,
        bool isMove,
        LogicalDeviceViewModel sourceDevice,
        LogicalDeviceViewModel targetDevice)
    {
        // Pasting onto an archive that is not the current listing skips MergeFiles (no DirList TOC).
        var skipMergeForForeignArchive = ArchiveHelper.CanPasteIntoArchive(targetPath, targetDevice.ID)
            && !IsExplorerListing(targetPath, targetDevice);

        IReadOnlySet<string> replacePaths = EmptyPathSet;
        IReadOnlySet<string> conflictPaths = EmptyPathSet;
        if (!skipMergeForForeignArchive)
        {
            var outcome = await MergeFiles(targetPath, transferItems, targetDevice, sourceDevice);
            transferItems = outcome.Items;
            replacePaths = outcome.ReplaceRelativePaths;
            conflictPaths = outcome.ConflictRelativePaths;
            if (outcome.Items.Count == 0)
                return;
        }

        try
        {
            await Task.Run(() => FileActionLogic.TransferItems(transferItems, targetPath, isMove, sourceDevice, targetDevice, replacePaths, conflictPaths));
        }
        catch (Exception e)
        {
            DialogService.ShowMessage(e.Message,
                                      Strings.Resources.S_DEST_ERR,
                                      DialogService.DialogIcon.Critical,
                                      copyToClipboard: true,
                                      error: DialogError.DestinationPathFailed);
        }
    }

    public async void VerifyAndPaste(DragDropEffects cutType,
                               string targetPath,
                               IEnumerable<FileClass> pasteItems,
                               Dispatcher dispatcher,
                               LogicalDeviceViewModel device,
                               string currentPath)
    {
        pasteItems = await RemoveAncestor(pasteItems, targetPath, cutType);
        if (!pasteItems.Any())
            return;

        // Same-folder self-copy keeps the " - Copy" rename path (no conflict dialog).
        // Archive extract and paste from elsewhere prompt via MergeFiles, then replace in place.
        // Pasting onto an archive that is not the current listing skips MergeFiles (no DirList TOC).
        var isArchiveSource = ArchiveExtract.IsArchiveSource(pasteItems, device.ID);
        var isSameFolderSelfCopy = !isArchiveSource
            && cutType is DragDropEffects.Copy
            && pasteItems.All(f => f.ParentPath == targetPath);

        var skipMergeForForeignArchive = ArchiveHelper.CanPasteIntoArchive(targetPath, device.ID)
            && !IsExplorerListing(targetPath, device);

        if (!isSameFolderSelfCopy && !skipMergeForForeignArchive)
        {
            var outcome = await MergeFiles(targetPath, pasteItems, device);
            pasteItems = outcome.Items;
            if (!pasteItems.Any())
                return;
        }

        // Archive sources: extract selected members (copy only — no in-archive cut yet).
        if (isArchiveSource)
        {
            if (ArchivePath.IsArchivePath(targetPath, device.ID))
                return;

            ShellFileOperation.ExtractItems(device: device,
                      items: pasteItems,
                      targetPath: targetPath,
                      dispatcher: dispatcher);
            return;
        }

        // Device paste into a modifiable tar archive.
        if (ArchiveHelper.CanPasteIntoArchive(targetPath, device.ID))
        {
            if (cutType is DragDropEffects.Link)
                return;

            ShellFileOperation.PasteItemsToTar(device, pasteItems, targetPath, dispatcher, cutType);
            return;
        }

        ShellFileOperation.MoveItems(device: device,
                  items: pasteItems,
                  targetPath: targetPath,
                  currentPath: currentPath,
                  existingItems: Data.Files.DirList?.FileList?.Select(f => f.FullName) ?? [],
                  dispatcher: dispatcher,
                  cutType: cutType);
    }

    /// <summary>
    /// Check for pasting in descendant or self
    /// </summary>
    public async Task<IEnumerable<FileClass>> RemoveAncestor(IEnumerable<FileClass> pasteItems, string targetPath, DragDropEffects cutType)
    {
        if (cutType is DragDropEffects.Link || !IsSelf)
            return pasteItems;

        var ancestor = pasteItems.FirstOrDefault(f => f.Relation(targetPath) is RelationType.Self or RelationType.Descendant);

        if (ancestor is null)
            return pasteItems;

        var result = await DialogService.ShowConfirmation(
            string.Format(Strings.Resources.S_PASTE_ANCESTOR, ancestor.FullName),
            string.Format(Strings.Resources.S_PASTE_CONFLICT, IsDrag ? Strings.Resources.S_DROP : Strings.Resources.S_PASTE),
            Strings.Resources.S_SKIP,
            cancelText: Strings.Resources.S_BUTTON_ABORT,
            icon: DialogService.DialogIcon.Exclamation);

        return result.Item1 is Wpf.Ui.Controls.ContentDialogResult.Primary
            ? pasteItems.Except([ancestor])
            : [];
    }
}
