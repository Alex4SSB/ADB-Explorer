using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

public class CopyPasteService : ViewModelBase
{
    [Flags]
    public enum DataSource
    {
        None = 0x8000,
        Android = 0x1,      // 0 for Windows
        Self = 0x2,         // 0 for other (including Android)
        Virtual = 0x4,      // 0 for immediately available files
    }

    private CutType pasteState = CutType.None;
    public CutType PasteState
    {
        get => pasteState;
        set
        {
            if (Set(ref pasteState, value))
            {
                Data.FileActions.IsCutState.Value = value is CutType.Cut;
                Data.FileActions.IsCopyState.Value = value is CutType.Copy;
            }
        }
    }

    private DataSource pasteSource = DataSource.None;
    public DataSource PasteSource
    {
        get => pasteSource;
        set
        {
            if (Set(ref pasteSource, value)
                && pasteSource.HasFlag(DataSource.None)
                && value is not DataSource.None)
            {
                // Remove the none flag when settings something else
                pasteSource &= ~DataSource.None;
            }
        }
    }

    private DataSource dragPasteSource = DataSource.None;
    public DataSource DragPasteSource
    {
        get => dragPasteSource;
        set
        {
            if (Set(ref dragPasteSource, value)
                && dragPasteSource.HasFlag(DataSource.None)
                && value is not DataSource.None)
            {
                // Remove the none flag when settings something else
                dragPasteSource &= ~DataSource.None;
            }
        }
    }

    public bool IsDrag => DragPasteSource is not DataSource.None;
    public bool IsClipboard => PasteSource is not DataSource.None && !IsDrag;
    public bool IsSelf => PasteSource.HasFlag(DataSource.Self);
    public bool IsSelfClipboard => IsSelf && IsClipboard;
    public bool IsWindows => !PasteSource.HasFlag(DataSource.None) && !PasteSource.HasFlag(DataSource.Android);
    public bool IsVirtual => PasteSource.HasFlag(DataSource.Virtual);

    private string parentFolder = "";
    public string ParentFolder
    {
        get => parentFolder;
        set => Set(ref parentFolder, value);
    }

    private string dragParent = "";
    public string DragParent
    {
        get => dragParent;
        set => Set(ref dragParent, value);
    }

    private string[] files = [];
    public string[] Files
    {
        get => files;
        set => Set(ref files, value);
    }

    private string[] dragFiles = [];
    public string[] DragFiles
    {
        get => dragFiles;
        set => Set(ref dragFiles, value);
    }

    public void UpdateUI()
    {
        FileActionLogic.UpdateFileActions();

        IEnumerable<FileClass> cutItems = [];
        if (PasteSource is not DataSource.None && PasteSource.HasFlag(DataSource.Self))
            cutItems = Data.DirList.FileList.Where(f => Files.Contains(f.FullPath));

        cutItems.ForEach(file => file.CutState = PasteState);
        Data.DirList?.FileList.Except(cutItems).ForEach(file => file.CutState = CutType.None);
    }

    public void Clear()
    {
        if (IsClipboard)
            Clipboard.Clear();

        PasteState = CutType.None;
        PasteSource = DataSource.None;
        Files = [];
        ParentFolder = "";

        ClearDrag();
        UpdateUI();
    }

    public void ClearDrag()
    {
        DragPasteSource = DataSource.None;
        DragFiles = [];
        DragParent = "";
    }

    public void GetClipboardPasteItems()
    {
        var CPDO = Clipboard.GetDataObject();
        
        var allowedEffect = GetAllowedDragEffects(CPDO);
        if (allowedEffect is DragDropEffects.None)
        {
            PasteState = CutType.None;
            PasteSource = DataSource.None;
            return;
        }

        var prefDropEffect = VirtualFileDataObject.GetPreferredDropEffect(CPDO);

        // Link is only allowed depending on the target
        if (prefDropEffect.HasFlag(DragDropEffects.Copy))
            PasteState = CutType.Copy;
        else if (prefDropEffect.HasFlag(DragDropEffects.Move))
            PasteState = CutType.Cut;
        else
            PasteState = CutType.None;

        Files = DragFiles;
        ParentFolder = DragParent;

        UpdateUI();
    }

    public DragDropEffects GetAllowedDragEffects(IDataObject dataObject, FrameworkElement sender = null)
    {
        if (sender is null)
        {
            PasteSource &= ~DataSource.None;
            DragPasteSource = DataSource.None;
        }
        else
            DragPasteSource &= ~DataSource.None;

        PreviewDataObject(dataObject);
        if (DragFiles.Length < 1)
            return DragDropEffects.None;

        var dataContext = sender?.DataContext;
        FileClass file = dataContext is FileClass fc ? fc : null;

        if (Data.FileActions.IsAppDrive)
        {
            // TODO: Add support for sources other than Windows
            if (!PasteSource.HasFlag(DataSource.Android) && FileHelper.AllFilesAreApks(DragFiles))
                return DragDropEffects.Copy;
        }
        else if (dataContext is null || file?.IsDirectory is true)
        {
            if (PasteSource.HasFlag(DataSource.Android))
            {
                // TODO: Add support for dragging from another device
                if (!PasteSource.HasFlag(DataSource.Self))
                    return DragDropEffects.None;

                // Only one file and trying to drag into itself
                if (file is not null && DragFiles.Length == 1 && DragFiles[0] == file.FullPath)
                    return DragDropEffects.None;
            }
            
            return DragDropEffects.Move | DragDropEffects.Copy;
        }

        return DragDropEffects.None;
    }

    public void PreviewDataObject(IDataObject dataObject)
    {
        if (IsDrag)
            DragPasteSource &= ~(DataSource.Android | DataSource.Self | DataSource.Virtual);
        else
            PasteSource &= ~(DataSource.Android | DataSource.Self | DataSource.Virtual);

        DragParent = "";
        string[] oldFiles = [.. DragFiles];

        if (dataObject.GetDataPresent(AdbDataFormats.FileDrop) && dataObject.GetData(AdbDataFormats.FileDrop) is string[] dropFiles)
        {
            DragFiles = dropFiles;
        }
        else if (dataObject.GetDataPresent(AdbDataFormats.AdbDrop) && dataObject.GetData(AdbDataFormats.AdbDrop) is MemoryStream adbStream)
        {
            var dragList = NativeMethods.ADBDRAGLIST.FromStream(adbStream);
            
            if (!IsDrag)
            {
                if (!Data.DevicesObject.UIList.Any(d => d.ID == dragList.deviceId && d.Status is AbstractDevice.DeviceStatus.Ok))
                {
                    Clear();
                    return;
                }
            }

            DragParent = dragList.parentFolder;
            DragFiles = dragList.items.Select(f => FileHelper.ConcatPaths(DragParent, f)).ToArray();
            
            PasteSource |= DataSource.Android;
            if (dragList.deviceId == Data.CurrentADBDevice.ID)
                PasteSource |= DataSource.Self;
            else
                PasteSource |= DataSource.Virtual;
        }
        else if (dataObject.GetDataPresent(AdbDataFormats.FileDescriptor) && dataObject.GetData(AdbDataFormats.FileDescriptor) is MemoryStream fdStream)
        {
            var fileGroup = NativeMethods.FILEGROUPDESCRIPTOR.FromStream(fdStream);
            DragFiles = fileGroup.descriptors.Select(d => d.cFileName).ToArray();

            PasteSource |= DataSource.Virtual;
        }

        if (oldFiles != DragFiles && IsDrag)
            UpdateUI();
    }

    public void AcceptDataObject(IDataObject dataObject, FrameworkElement sender, bool isLink = false)
    {
        var dataContext = sender.DataContext;

        string targetFolder = dataContext is FileClass file && file.IsDirectory
            ? file.FullPath
            : Data.CurrentPath;
        AcceptDataObject(dataObject, targetFolder, isLink);
    }

    public void AcceptDataObject(IDataObject dataObject, IEnumerable<FileClass> selectedFiles, bool isLink = false)
    {
        string targetFolder = selectedFiles.Count() == 1 && selectedFiles.First().IsDirectory
            ? selectedFiles.First().FullPath
            : Data.CurrentPath;
        AcceptDataObject(dataObject, targetFolder, isLink);
    }

    public void AcceptDataObject(IDataObject dataObject, string targetFolder, bool isLink = false)
    {
        void ReadObject()
        {
            if (Data.FileActions.IsAppDrive)
            {
                if (IsWindows && !IsVirtual && FileHelper.AllFilesAreApks(DragFiles))
                    ShellFileOperation.PushPackages(Data.CurrentADBDevice, DragFiles.Select(ShellObject.FromParsingName), App.Current.Dispatcher);

                return;
            }

            if (IsVirtual)
            {
                if (!IsWindows)
                {
                    // TODO: add support for Android to Android transfers
                }
                else if (dataObject.GetDataPresent(AdbDataFormats.FileDescriptor)
                    && dataObject.GetDataPresent(AdbDataFormats.FileContents))
                {
                    // TODO: add support for writing virtual streams to files in the temp folder and then pushing them
                }
            }
            else if (IsWindows)
            {
                VerifyAndPush(targetFolder, DragFiles);

                if (PasteState is CutType.Cut)
                {
                    // TODO: handle cut from File Explorer - notify of the operation completion and if not enough, clear the CB and delete the files
                }
            }
            else if (IsSelf)
            {
                // Dragging a folder into itself is not allowed
                if (DragFiles.Length == 1 && DragFiles[0] == targetFolder && IsDrag)
                    return;

                VerifyAndPaste(isLink ? CutType.Link : PasteState,
                               targetFolder,
                               DragFiles,
                               App.Current.Dispatcher,
                               Data.CurrentADBDevice,
                               Data.CurrentPath);
                
                if (PasteState is CutType.Cut)
                    Clear();
            }
        }

        ReadObject();
        if (IsDrag)
            ClearDrag();
    }

    public static async void VerifyAndPush(string targetPath, IEnumerable<string> pasteItems)
    {
        pasteItems = await MergeFiles(pasteItems, targetPath);
        if (!pasteItems.Any())
            return;

        FileActionLogic.PushShellObjects(pasteItems.Select(ShellObject.FromParsingName), targetPath);
    }

    public async void VerifyAndPaste(CutType cutType,
                               string targetPath,
                               IEnumerable<string> pasteItems,
                               Dispatcher dispatcher,
                               ADBService.AdbDevice device,
                               string currentPath)
    {
        pasteItems = await RemoveAncestor(pasteItems, targetPath, cutType);
        if (!pasteItems.Any())
            return;

        pasteItems = await MergeFiles(pasteItems, targetPath);
        if (!pasteItems.Any())
            return;

        ShellFileOperation.MoveItems(device: device,
                  items: pasteItems,
                  targetPath: targetPath,
                  currentPath: currentPath,
                  dispatcher: dispatcher,
                  cutType: cutType);
    }

    public static async Task<IEnumerable<string>> MergeFiles(IEnumerable<string> fileNames, string targetPath)
    {
        // Figure out whether the target is Windows or Android
        var sep = FileHelper.GetSeparator(targetPath);
        IEnumerable<string> existingItems = [];
        var fuse = DriveHelper.GetCurrentDrive(targetPath).IsFUSE;
        StringComparison comparisonType = StringComparison.InvariantCultureIgnoreCase;

        if (sep == '/') // Android
        {
            comparisonType = fuse ? StringComparison.InvariantCultureIgnoreCase : StringComparison.InvariantCulture;

            if (targetPath == Data.CurrentPath)
            {
                existingItems = Data.DirList.FileList.Where(f => fileNames.Any(name => FileHelper.GetFullName(name).Equals(f.FullName, comparisonType))).Select(f => f.FullPath);
            }
            else
            {
                existingItems = ADBService.FindFilesInPath(Data.CurrentADBDevice.ID, targetPath, includeNames: fileNames.Select(FileHelper.GetFullName), caseSensitive: !fuse);
            }
        }
        else // Windows
        {
            var files = Directory.GetFiles(targetPath);
            var dirs = Directory.GetDirectories(targetPath);

            existingItems = dirs.Concat(files).Where(f => fileNames.Any(name => FileHelper.GetFullName(name).Equals(FileHelper.GetFullName(f), comparisonType)));
        }

        var count = existingItems.Count();
        if (count > 0)
        {
            string destination = FileHelper.GetFullName(targetPath);
            if (Data.CurrentDisplayNames.TryGetValue(targetPath, out var drive))
                destination = drive;

            var result = await DialogService.ShowConfirmation(
                $"{Strings.S_CONFLICT_ITEMS(count)} in {destination}",
                "Paste Conflicts",
                primaryText: "Merge or Replace",
                secondaryText: count == fileNames.Count() ? "" : "Skip",
                cancelText: "Cancel",
                icon: DialogService.DialogIcon.Exclamation);

            if (result.Item1 is ContentDialogResult.None) // Cancel
            {
                return [];
            }
            else if (result.Item1 is ContentDialogResult.Secondary) // Skip
            {
                fileNames = fileNames.Where(item => !existingItems.Contains(FileHelper.GetFullName(item)));
            }
        }
        
        return fileNames;
    }

    /// <summary>
    /// Check for pasting in descendant or self
    /// </summary>
    public async Task<IEnumerable<string>> RemoveAncestor(IEnumerable<string> pasteItems, string targetPath, CutType cutType)
    {
        if (cutType is CutType.Link || !IsSelf)
            return pasteItems;

        var ancestor = pasteItems.FirstOrDefault(f => FileHelper.RelationFrom(f, targetPath) is RelationType.Self or RelationType.Descendant);

        if (ancestor is null)
            return pasteItems;

        var result = await DialogService.ShowConfirmation(
            Strings.S_PASTE_ANCESTOR(ancestor),
            $"{(IsDrag ? "Drop" : "Paste")} Conflict",
            "Skip",
            cancelText: "Abort",
            icon: DialogService.DialogIcon.Exclamation);

        return result.Item1 is ContentDialogResult.Primary
            ? pasteItems.Except([ancestor])
            : [];
    }
}
