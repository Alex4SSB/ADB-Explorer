using Vanara.Windows.Shell;

namespace ADB_Explorer.Services;

public static partial class ShellFileOperation
{
    /// <summary>
    /// Extracts archive selections to <paramref name="targetPath"/> (device paste from archive clipboard).
    /// Caller must have already resolved name conflicts (merge/replace/skip); existing targets are replaced.
    /// </summary>
    public static void ExtractItems(LogicalDeviceViewModel device,
                                    IEnumerable<FileClass> items,
                                    string targetPath,
                                    Dispatcher dispatcher)
    {
        items = [.. items];
        List<FileExtractOperation> fileops = [];

        foreach (var item in items)
        {
            if (!ArchivePath.TryParse(item.FullPath, out _, out _, device.ID))
                continue;

            SyncFile target = new(FileHelper.ConcatPaths(targetPath, item.FullName), item.Type);
            fileops.Add(new(item, target, device, dispatcher));
        }

        if (fileops.Count == 0)
            return;

        dispatcher.Invoke(() =>
        {
            fileops.ForEach(op => op.WhenCompleted(() => OnExtractCompleted(op)));
            Data.FileOpQ.AddOperations(fileops);
        });
    }

    /// <summary>
    /// Pastes device files into a modifiable tar archive (extract + overlay + repack).
    /// </summary>
    public static FileArchiveModifyOperation? PasteItemsToTar(
        LogicalDeviceViewModel device,
        IEnumerable<FileClass> items,
        string archiveTargetComposite,
        Dispatcher dispatcher,
        DragDropEffects cutType = DragDropEffects.Copy)
    {
        List<FileClass> list = [.. items];
        if (list.Count == 0)
            return null;

        var op = FileArchiveModifyOperation.FromDevicePaste(list, archiveTargetComposite, device, dispatcher, cutType);
        dispatcher.Invoke(() =>
        {
            op.WhenCompleted(() => OnArchiveModifyCompleted(op));
            Data.FileOpQ.AddOperation(op);
        });

        return op;
    }

    /// <summary>
    /// Pushes Windows items into a modifiable tar archive (extract + push overlay + repack).
    /// </summary>
    public static void PushItemsToTar(
        LogicalDeviceViewModel device,
        IEnumerable<ShellItem> items,
        string archiveTargetComposite,
        Dispatcher dispatcher)
    {
        List<ShellItem> list = [.. items];
        if (list.Count == 0)
            return;

        var op = FileArchiveModifyOperation.FromWindowsPush(list, archiveTargetComposite, device, dispatcher);
        dispatcher.Invoke(() =>
        {
            op.WhenCompleted(() => OnArchiveModifyCompleted(op));
            Data.FileOpQ.AddOperation(op);
        });
    }

    private static void OnArchiveModifyCompleted(FileArchiveModifyOperation op)
    {
        foreach (var src in op.DeviceSources)
            src.CutState = DragDropEffects.None;

        if (op.Device.ID == Data.ActiveDevice?.ID
            && ArchivePath.TryParse(Data.CurrentPath, out var currentArchive, out _, op.Device.ID)
            && currentArchive == op.TarArchivePath)
        {
            App.SafeBeginInvoke(FileActionLogic.Refresh);
            FileActionLogic.UpdateFileActions();
        }
    }

    private static void OnExtractCompleted(FileExtractOperation op)
    {
        op.FilePath.CutState = DragDropEffects.None;

        var extracted = Data.ForEachListingAt(op.TargetPath.ParentPath, op.Device.ID, (instance, isFocused) =>
        {
            FileClass newFile = new(op.FilePath);
            newFile.UpdatePath(op.TargetPath.FullPath);
            instance.FileList.DirList!.FileList.Add(newFile);

            if (isFocused && Data.FileOpQ.TotalCount == 1)
                Data.ItemToSelect.Value = newFile;
        });

        if (extracted)
            FileActionLogic.UpdateFileActions();
    }

    /// <summary>
    /// Creates a tar-family archive at <paramref name="archiveFile"/> from <paramref name="sourcePaths"/>.
    /// </summary>
    public static void CompressArchive(
        LogicalDeviceViewModel device,
        FileClass archiveFile,
        IReadOnlyList<string> sourcePaths,
        Dispatcher dispatcher)
    {
        var op = new FileCompressOperation(archiveFile, sourcePaths, device, dispatcher);
        dispatcher.Invoke(() =>
        {
            op.WhenFinished(status => OnCompressFinished(op, status));
            Data.FileOpQ.AddOperation(op);
        });
    }

    private static void OnCompressFinished(FileCompressOperation op, FileOperation.OperationStatus status)
    {
        if (op.Device.ID != Data.ActiveDevice?.ID || op.FilePath.ParentPath != Data.CurrentPath)
            return;

        // A failed or canceled archive leaves nothing behind, so its placeholder goes too.
        if (status is FileOperation.OperationStatus.Completed)
        {
            op.FilePath.UpdateType();
            _ = op.FilePath.UpdateExtraInfoAsync(CancellationToken.None);
        }
        else
        {
            Data.DirList!.FileList.Remove(op.FilePath);
        }

        FileActionLogic.UpdateFileActions();
    }
}
