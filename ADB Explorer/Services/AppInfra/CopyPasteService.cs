using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using ADB_Explorer.ViewModels.Pages;
using Vanara.Windows.Shell;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

public partial class CopyPasteService : ObservableObject
{
    [Flags]
    public enum DataSource
    {
        None = 0x8000,
        Android = 0x1,      // 0 for Windows
        Self = 0x2,         // 0 for other (including Android)
        Virtual = 0x4,      // 0 for immediately available files
    }

    public enum DragState
    {
        None,
        Pending,
        Active,
    }

    public DragDropEffects PasteState
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                Data.FileActions.IsCutState.Value = value is DragDropEffects.Move;
                Data.FileActions.IsCopyState.Value = value is DragDropEffects.Copy;
            }
        }
    } = DragDropEffects.None;

    [ObservableProperty]
    public partial DragDropEffects DropEffect { get; set; } = DragDropEffects.None;

    [ObservableProperty]
    public partial DragDropEffects CurrentDropEffect { get; set; } = DragDropEffects.None;

    [ObservableProperty]
    public partial string? DropTarget { get; set; } = null;

    [ObservableProperty]
    public partial LogicalDeviceViewModel? DropTargetDevice { get; set; }

    public string DropTargetName
    {
        get
        {
            if (DropTarget is null)
                return "";

            string destination = FileHelper.GetFullName(DropTarget);
            if (Data.CurrentDisplayNames.TryGetValue(DropTarget, out var drive))
                destination = drive;

            return destination;
        }
    }

    public DataSource PasteSource
    {
        get;
        set
        {
            if (SetProperty(ref field, value)
                && field.HasFlag(DataSource.None)
                && value is not DataSource.None)
            {
                // Remove the none flag when setting something else
                field &= ~DataSource.None;
            }
        }
    } = DataSource.None;

    public DataSource DragPasteSource
    {
        get;
        set
        {
            if (SetProperty(ref field, value)
                && field.HasFlag(DataSource.None)
                && value is not DataSource.None)
            {
                // Remove the none flag when settings something else
                field &= ~DataSource.None;
            }
        }
    } = DataSource.None;

    [ObservableProperty]
    public partial DragState DragStatus { get; set; } = DragState.None;

    public bool IsDrag => DragPasteSource is not DataSource.None;
    public bool IsClipboard => PasteSource is not DataSource.None && !IsDrag;
    
    public DataSource CurrentSource
    {
        get => IsDrag ? DragPasteSource : PasteSource;
        set
        {
            if (IsDrag)
                DragPasteSource = value;
            else
                PasteSource = value;
        }
    }

    [ObservableProperty]
    public partial BitmapSource? DragBitmap { get; set; } = null;

    [ObservableProperty]
    public partial bool DragWithinSlave { get; set; } = false;

    [ObservableProperty]
    public partial bool MouseWithinApp { get; set; } = true;

    public NativeMethods.HResult DragResult { get; set; }

    /// <summary>
    /// True from the start of an OLE drag until the next listing/tree mouse-down.
    /// Used to skip the context menu that would otherwise open after a right-click cancel.
    /// </summary>
    public bool WasDragging { get; set; }

    public DragDropEffects CurrentEffect => IsDrag ? DropEffect : PasteState;
    public string CurrentParent => IsDrag ? DragParent : ParentFolder;
    public bool IsSelf => CurrentSource.HasFlag(DataSource.Self);
    public bool IsSelfClipboard => IsSelf && IsClipboard;
    public bool IsWindows => !CurrentSource.HasFlag(DataSource.None) && !CurrentSource.HasFlag(DataSource.Android);
    public bool IsVirtual => CurrentSource.HasFlag(DataSource.Virtual);

    [ObservableProperty]
    public partial string ParentFolder { get; set; } = "";

    [ObservableProperty]
    public partial string DragParent { get; set; } = "";

    [ObservableProperty]
    public partial string[] Files { get; set; } = [];

    public string[] DragFiles
    {
        get;
        set
        {
            // The Set method only compares instances
            if (field.SequenceEqual(value))
                return;

            SetProperty(ref field, value);
            _currentFiles = null;
        }
    } = [];

    public FileDescriptor[] Descriptors
    {
        get;
        set
        {
            // The Set method only compares instances
            if (field.SequenceEqual(value))
                return;

            SetProperty(ref field, value);
            _currentFiles = null;
        }
    } = [];

    private IEnumerable<FileClass>? _currentFiles = [];
    public IEnumerable<FileClass> CurrentFiles
    {
        get
        {
            _currentFiles ??= GetCurrentFiles();

            return _currentFiles;
        }
    }

    private IEnumerable<FileClass> GetCurrentFiles()
    {
        if (IsWindows && !IsVirtual)
        {
            foreach (var file in DragFiles)
            {
                yield return new(ShellItem.Open(file));
            }
        }
        else
        {
            if (IsSelf && VirtualFileDataObject.SelfFiles is not null)
            {
                foreach (var file in VirtualFileDataObject.SelfFiles)
                {
                    yield return file;
                }

                yield break;
            }

            for (int i = 0; i < Descriptors.Length; i++)
            {
                TrashIndexer indexer = null;
                if (DragFiles.Length == Descriptors.Length && CurrentParent is AdbExplorerConst.RECYCLE_PATH)
                    indexer = new() { RecycleName = DragFiles[i] };

                var desc = Descriptors[i];
                desc.SourcePath = FileHelper.ConcatPaths(CurrentParent, desc.Name);
                yield return new(desc)
                {
                    PathType = IsWindows
                        ? FilePathType.Windows
                        : FilePathType.Android,
                    TrashIndex = indexer,
                };
            }
        }
    }

    public int MasterPid { get; private set; }

    public bool IsDragFromMaster => MasterPid != Environment.ProcessId;

    public LogicalDeviceViewModel? SourceDevice { get; private set; }

    public static string UserTemp => $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\Temp\\";

    public void UpdateUI()
    {
        FileActionLogic.UpdateFileActions();

        var listing = Data.Files.DirList?.FileList;
        if (listing is not null)
        {
            List<FileClass> cutItems = [];
            var listingDevice = Data.Files.Device ?? Data.DevicesObject.Current;
            if (PasteSource is not DataSource.None && IsFromDevice(listingDevice))
                cutItems = [.. listing.Where(f => ContainsPath(f.FullPath))];

            cutItems.ForEach(file => file.CutState = PasteState);
            listing.Except(cutItems).ForEach(file => file.CutState = DragDropEffects.None);
        }

        App.Services.GetService<ExplorerViewModel>()?.Tree.UpdateCutStates();
    }

    public void Clear()
    {
        if (IsClipboard)
        {
            Clipboard.Clear();
            PasteState = DragDropEffects.None;
            PasteSource = DataSource.None;
            Files = [];
            ParentFolder = "";
            SourceDevice = null;
        }

        ClearDrag();
        UpdateUI();
        ArchiveExtract.BeginCleanupAllStaging();
    }

    public void ClearDrag()
    {
        if (!IsDrag)
            return;

        DragBitmap = null;
        if (IsClipboard)
            return;

        DropEffect = DragDropEffects.None;
        DragPasteSource = DataSource.None;
        DragFiles = [];
        DragParent = "";
        ArchiveExtract.BeginCleanupAllStaging();
    }

    public bool IsFromDevice(LogicalDeviceViewModel? device)
    {
        if (device is null)
            return IsSelf;

        if (SourceDevice is not null)
            return SourceDevice.ID == device.ID;

        return IsSelf;
    }

    public bool ContainsPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || Files.Length == 0)
            return false;

        foreach (var file in Files)
        {
            if (NavigationTreeNode.PathsEqual(file, path))
                return true;
        }

        return false;
    }

    private bool ShouldKeepSelfAndroidClipboard(IDataObject dataObject)
    {
        if (!CurrentSource.HasFlag(DataSource.Android)
            || MasterPid != Environment.ProcessId
            || Files.Length == 0)
            return false;

        if (dataObject.GetDataPresent(AdbDataFormats.AdbDrop)
            && dataObject.GetData(AdbDataFormats.AdbDrop) is MemoryStream)
            return false;

        // A new Windows/shell copy typically includes FileDrop. OLE can omit AdbDrop
        // on the first update while still holding our FileDescriptor payload.
        return !dataObject.GetDataPresent(AdbDataFormats.FileDrop);
    }

    private static bool IsAndroidAbsolutePath(string path)
        => !string.IsNullOrEmpty(path) && path[0] == '/';

    public void GetClipboardPasteItems()
    {
        var CPDO = Clipboard.GetDataObject();

#if !DEPLOY
        DebugLog.PrintLine($"Clipboard formats: {string.Join(", ", CPDO.GetFormats())}");
#endif

        if (ShouldKeepSelfAndroidClipboard(CPDO))
        {
            UpdateUI();
            return;
        }

        var allowedEffect = GetAllowedDragEffects(CPDO);
        if (allowedEffect is DragDropEffects.None)
        {
            PasteState = DragDropEffects.None;
            PasteSource = DataSource.None;
            Files = [];
            _currentFiles = [];

            UpdateUI();
            ArchiveExtract.BeginCleanupAllStaging();
            return;
        }

        var prefDropEffect = VirtualFileDataObject.GetPreferredDropEffect(CPDO);

        // Link is only allowed depending on the target
        if (prefDropEffect.HasFlag(DragDropEffects.Link))
            PasteState = DragDropEffects.Link;
        else if (prefDropEffect.HasFlag(DragDropEffects.Copy) && allowedEffect.HasFlag(DragDropEffects.Copy))
            PasteState = DragDropEffects.Copy;
        else if (prefDropEffect.HasFlag(DragDropEffects.Move) && allowedEffect.HasFlag(DragDropEffects.Move))
            PasteState = DragDropEffects.Move;
        else if (prefDropEffect is DragDropEffects.Move && allowedEffect is DragDropEffects.Copy)
            PasteState = DragDropEffects.Copy; // fallback to copy
        else
            PasteState = DragDropEffects.None;

        if (DragFiles.Length > 0 && DragFiles.All(IsAndroidAbsolutePath))
            Files = DragFiles;
        else if (!CurrentSource.HasFlag(DataSource.Android) || Files.Length == 0)
            Files = DragFiles;

        if (!string.IsNullOrEmpty(DragParent))
            ParentFolder = DragParent;

        UpdateUI();

        // External clipboard replaced a self archive copy — drop unused extract staging.
        if (!IsSelf)
            ArchiveExtract.BeginCleanupAllStaging();
    }

    public void UpdateSelfVFDO(bool isDrag, DragDropEffects pasteEffect = DragDropEffects.None)
    {
        if (VirtualFileDataObject.SelfFiles is null || !VirtualFileDataObject.SelfFiles.Any())
            return;

        if (isDrag)
        {
            DragPasteSource = (DragPasteSource | DataSource.Android | DataSource.Self) & ~DataSource.None;
        }
        else
        {
            PasteSource = (PasteSource | DataSource.Android | DataSource.Self) & ~DataSource.None;
            if (pasteEffect is not DragDropEffects.None)
                PasteState = pasteEffect;
        }

        var copyDevice = Data.Active.Device ?? Data.DevicesObject.Current;
        if (copyDevice is not null
            && (SourceDevice is null || !ReferenceEquals(Data.Active, Data.Files)))
            SourceDevice = copyDevice;
        MasterPid = Environment.ProcessId;
        var transferParent = FileHelper.GetSearchTransferParent(VirtualFileDataObject.SelfFiles);
        if (Data.FileActions.IsSearchMode)
            Data.SearchTransferParent = transferParent;

        DragParent = transferParent;

        // FileDescriptors may still be the empty placeholder while PrepareDescriptors runs
        // (especially archive extract-to-tmp). Fall back to SelfFiles until they are ready.
        var descriptors = VirtualFileDataObject.SelfFileGroup?.FileDescriptors?.ToArray();
        if (descriptors is { Length: > 0 })
        {
            DragFiles = [.. descriptors.Select(d => d.Name)];
            Descriptors = descriptors;
        }
        else
        {
            DragFiles = [.. VirtualFileDataObject.SelfFiles.Select(FileHelper.GetSearchTransferName)];
            Descriptors = [];
        }

        if (!isDrag)
        {
            Files = [.. VirtualFileDataObject.SelfFiles.Select(f => f.FullPath)];
            ParentFolder = DragParent;
        }

        UpdateUI();
    }

    public DragDropEffects GetAllowedDragEffects(IDataObject dataObject, FrameworkElement? sender = null)
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

        // Clipboard evaluation (no drop target): keep the paste payload even when the
        // current location cannot accept it (e.g. still inside a read-only archive).
        // EnableUiPaste / EnableKeyboardPaste re-check the real destination on navigate.
        if (sender is null)
        {
            if (Data.FileActions.IsAppDrive)
            {
                if (IsSelf)
                    return DragDropEffects.None;

                return FileHelper.AllFilesAreApks(DragFiles) ? DragDropEffects.Copy : DragDropEffects.None;
            }

            if (CurrentSource.HasFlag(DataSource.Virtual) && !CurrentSource.HasFlag(DataSource.Android))
                return DragDropEffects.Copy;

            return DragDropEffects.Move | DragDropEffects.Copy;
        }

        var dataContext = sender.DataContext;
        FileClass file = dataContext is FileClass fc ? fc : null;

        if (Data.FileActions.IsAppDrive)
        {
            // App drive drag is pull-to-elsewhere only — never install/move onto itself.
            if (IsSelf)
                return DragDropEffects.None;

            if (FileHelper.AllFilesAreApks(DragFiles))
                return DragDropEffects.Copy;
        }
        else if (file is null
            || file.IsDirectory
            || ArchiveHelper.CanPasteIntoArchiveFile(file.IsLink ? file.LinkTarget : file.FullPath, Data.DevicesObject.Current?.ID ?? ""))
        {
            string rawPath;
            if (file is null)
                rawPath = Data.CurrentPath;
            else
                rawPath = file.IsLink ? file.LinkTarget : file.FullPath;

            var deviceId = Data.DevicesObject.Current?.ID ?? "";
            var targetPath = ArchiveHelper.ResolvePasteTargetPath(rawPath, deviceId);
            Data.CopyPaste.DropTarget = targetPath;
            Data.CopyPaste.DropTargetDevice = Data.DevicesObject.Current;

            if (!DriveHelper.IsModificationAllowedAt(targetPath, deviceId))
                return DragDropEffects.None;

            if (ArchivePath.IsArchivePath(targetPath, deviceId)
                && !ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
                return DragDropEffects.None;

            if (CurrentSource.HasFlag(DataSource.Android))
            {
                if (IsDrag)
                    return FileActionLogic.EnableDropPaste(file);
            }
            else if (CurrentSource.HasFlag(DataSource.Virtual))
                return DragDropEffects.Copy;
            
            // Windows filesystem drop: Copy|Move into tar, never Link.
            if (ArchiveHelper.CanPasteIntoArchive(targetPath, deviceId))
                return DragDropEffects.Copy | DragDropEffects.Move;

            return DragDropEffects.Move | DragDropEffects.Copy;
        }

        return DragDropEffects.None;
    }

    public void PreviewDataObject(IDataObject dataObject)
    {
        CurrentSource &= ~(DataSource.Android | DataSource.Self | DataSource.Virtual);

        DragParent = "";
        string[] oldFiles = [.. DragFiles];

        // ADB Drop - for all Android to Android transfers (including self)
        if (dataObject.GetDataPresent(AdbDataFormats.AdbDrop) && dataObject.GetData(AdbDataFormats.AdbDrop) is MemoryStream adbStream)
        {
            var dragList = NativeMethods.ADBDRAGLIST.FromStream(adbStream);
            var deviceId = dragList.deviceId;

            var device = Data.DevicesObject.UIList.OfType<LogicalDeviceViewModel>().FirstOrDefault(d => d.ID == deviceId && d.Status is DeviceStatus.Ok);
            if (device is null
                && SourceDevice?.ID == deviceId
                && MasterPid == Environment.ProcessId)
                device = SourceDevice;

            if (!IsDrag && device is null)
            {
                Clear();
                return;
            }

            SourceDevice = device;

            MasterPid = dragList.pid;
            DragParent = dragList.parentFolder;
            DragFiles = [.. dragList.items.Select(f => FileHelper.ConcatPaths(DragParent, f))];

            CurrentSource |= DataSource.Android;
            var currentId = Data.DevicesObject.Current?.ID;
            if (deviceId == currentId)
                CurrentSource |= DataSource.Self;
            else if (currentId is null && dragList.pid == Environment.ProcessId)
                CurrentSource |= DataSource.Self;
            else
                CurrentSource |= DataSource.Virtual;

            if (dataObject.GetDataPresent(AdbDataFormats.FileDescriptor))
            {
                // Descriptors are filled asynchronously (and for archives only after extract-to-tmp).
                // Retry until ready; empty placeholder bytes must not be treated as a real group.
                Task.Run(async () =>
                {
                    for (var attempt = 0; attempt < 40; attempt++)
                    {
                        await Task.Delay(250).ConfigureAwait(false);

                        FileDescriptor[]? fds = null;
                        App.SafeInvoke(() => fds = FileDescriptor.GetDescriptors(dataObject));
                        if (fds is not { Length: > 0 })
                            continue;

                        App.SafeInvoke(() =>
                        {
                            Descriptors = fds;
                            UpdateUI();
                        });
                        return;
                    }
                });
            }
        }
        // Shell ID List - the only format Microsoft supports for anything added after Windows XP (non-ZIP archives, UNC paths, etc.)
        else if (dataObject.GetDataPresent(AdbDataFormats.ShellidList))
        {
            SourceDevice = null;
            var ido = (System.Runtime.InteropServices.ComTypes.IDataObject)dataObject;
            ShellItemArray? shItems = null;

            try
            {
                shItems = ShellItemArray.FromDataObject(ido);
            }
            catch (Exception e) // E_ACCESS_DENIED may be thrown if the data object has already been disposed by the source, but not from the clipboard
            {
#if !DEPLOY
                DebugLog.PrintLine($"Failed to get ShellItemArray from IDataObject: {e}");
#endif
            }

            if (shItems is not null)
            {
                Descriptors = [.. shItems.Select(sh => new FileDescriptor(sh))];
                DragFiles = [.. shItems.Select(sh => sh.ParsingName)];

                CurrentSource &= ~DataSource.Android;
                if (!shItems[0].IsFileSystem)
                    CurrentSource |= DataSource.Virtual;
            }
        }
        // VFDO (FileGroupDescriptor + FileContents) - the only viable format for virtual files not mapped to a drive.
        // This is the format we supply to File Explorer. Also provided by File Explorer for contents of ZIP archives (introduced in Windows ME).
        else if (dataObject.GetDataPresent(AdbDataFormats.FileDescriptor))
        {
            SourceDevice = null;
            GetDescriptors(dataObject);

            DragFiles = [.. Descriptors.Where(d => !d.Name.Contains('\\')).Select(d => d.Name)];

            CurrentSource |= DataSource.Virtual;
            if (dataObject.GetDataPresent(AdbDataFormats.FileContents))
                CurrentSource &= ~DataSource.Android;
        }
        // If the data object only has FileDrop, then it's probably dropping by target detect, which we can't support (7-Zip, WinRAR, etc.)
        else
        {
            SourceDevice = null;
            DragFiles = [];
            UpdateUI();
        }

        if (oldFiles != DragFiles && IsDrag)
            UpdateUI();
    }

    public void GetDescriptors(IDataObject dataObject)
    {
        var fds = FileDescriptor.GetDescriptors(dataObject);
        if (fds is not null)
        {
            Descriptors = fds;
            UpdateUI();
        }
    }

    public void AcceptDataObject(System.Windows.DragEventArgs e, FrameworkElement sender)
    {
        var dataContext = sender.DataContext;
        var deviceId = Data.DevicesObject.Current?.ID ?? "";

        string targetFolder;
        if (dataContext is FileClass file && ArchiveHelper.IsPasteTargetContainer(file, deviceId))
        {
            var path = file.IsLink ? file.LinkTarget : file.FullPath;
            targetFolder = ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
        }
        else
            targetFolder = Data.CurrentPath;

        if (Data.FileActions.IsSearchMode
            && !(dataContext is FileClass dropTarget && ArchiveHelper.IsPasteTargetContainer(dropTarget, deviceId))
            && FileHelper.IsSearchLocation(targetFolder))
            return;
        
        // Do not perform implicit duplicate by drag (only with Ctrl)
        if (IsSelf && targetFolder == DragParent && e.KeyStates is DragDropKeyStates.None)
            return;

        AcceptDataObject(e.Data, targetFolder, e.KeyStates.HasFlag(DragDropKeyStates.AltKey));
    }

    public void AcceptDataObject(IDataObject dataObject, IEnumerable<FileClass> selectedFiles, bool isLink = false)
    {
        var deviceId = Data.DevicesObject.Current?.ID ?? "";
        string targetFolder;
        if (selectedFiles.Count() == 1
            && selectedFiles.First() is { } item
            && ArchiveHelper.IsPasteTargetContainer(item, deviceId))
        {
            var path = item.IsLink ? item.LinkTarget : item.FullPath;
            targetFolder = ArchiveHelper.ResolvePasteTargetPath(path, deviceId);
        }
        else
            targetFolder = Data.CurrentPath;

        AcceptDataObject(dataObject, targetFolder, isLink);
    }

    public DragDropEffects GetAllowedTreeDropEffects(IDataObject dataObject, NavigationTreeNode node)
    {
        // Marks this as an active drag (routes CurrentSource through DragPasteSource instead of the
        // clipboard's PasteSource) so IsWindows/IsVirtual/IsSelf reflect the data actually being dragged,
        // not whatever was last copied. A self-initiated tree drag already did this via UpdateSelfVFDO,
        // but an external Explorer drag never goes through that, so it must happen here too.
        DragPasteSource &= ~DataSource.None;

        PreviewDataObject(dataObject);
        if (DragFiles.Length < 1)
            return DragDropEffects.None;

        return FileActionLogic.EnableTreeDropPaste(node);
    }

    public void AcceptTreeDrop(System.Windows.DragEventArgs e, NavigationTreeNode node)
    {
        var allowed = GetAllowedTreeDropEffects(e.Data, node);
        if (allowed is DragDropEffects.None)
            return;

        var device = node.OwnerDevice ?? Data.DevicesObject.Current;
        var deviceId = device?.ID ?? "";
        var targetFolder = ArchiveHelper.ResolvePasteTargetPath(node.DropTargetPath, deviceId);
        var isAppDrive = node.Drive?.Type is AbstractDrive.DriveType.Package;

        if (IsFromDevice(device) && targetFolder == DragParent && e.KeyStates is DragDropKeyStates.None)
            return;

        AcceptDataObject(e.Data, targetFolder, e.KeyStates.HasFlag(DragDropKeyStates.AltKey), device, isAppDrive);
    }

    public void AcceptDataObject(IDataObject dataObject, string targetFolder, bool isLink = false)
        => AcceptDataObject(dataObject, targetFolder, isLink, Data.DevicesObject.Current, Data.FileActions.IsAppDrive);

    public void AcceptDataObject(IDataObject dataObject, string targetFolder, bool isLink, LogicalDeviceViewModel? device, bool isAppDrive)
    {
        var deviceId = device?.ID ?? "";
        if (device is null || FileHelper.IsSearchLocation(targetFolder))
            return;

        // Packages pulled from app drive are not dropped back onto it.
        if (isAppDrive && IsSelf && Data.FileActions.IsAppDrive)
            return;

        if (!DriveHelper.IsModificationAllowedAt(targetFolder, deviceId) && !isAppDrive)
            return;

        // Symlink into archives is not supported.
        if (isLink && ArchivePath.IsArchivePath(targetFolder, deviceId))
            return;

        if ((isLink || CurrentEffect is DragDropEffects.Link)
            && DriveHelper.GetRestrictions(targetFolder, device).NoSymbolicLinks)
            return;

        void ReadObject()
        {
            var fromOtherAndroid = CurrentSource.HasFlag(DataSource.Android)
                && SourceDevice is not null
                && SourceDevice.ID != device.ID;

            // Virtual payload, or Android files that are not already on the drop target.
            // Self+Android with no explorer device is not Virtual, but is still a cross-device copy.
            if (fromOtherAndroid || (IsVirtual && SourceDevice?.ID != device.ID))
            {
                ClearTempFolder();

                // Transfer from another Android device
                if (fromOtherAndroid || !IsWindows)
                {
                    foreach (var item in CurrentFiles)
                    {
                        SyncFile target = new(item) { PathType = FilePathType.Windows };
                        target.UpdatePath(FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, item.FullName, '\\'));

                        FolderTree[]? children = null;
                        if (item.IsDirectory)
                            children = item.GetChildren(SourceDevice!.ID);

                        // Pull the file from the source device to the temp folder
                        var pullOp = FileSyncOperation.PullFile(new(item, children), target, SourceDevice!, App.AppDispatcher);
                        pullOp.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName != nameof(FileSyncOperation.Status)
                                || pullOp.Status is not FileOperation.OperationStatus.Completed)
                                return;

                            // Once done, create a shell item and push it to the target device (current)
                            FileClass file = new(target) { ShellItem = ShellItem.Open(target.FullPath) };
                            if (isAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(device, [file.ShellItem], App.AppDispatcher);

                                return;
                            }

                            var pushOp = VerifyAndPush(targetFolder, file, CurrentEffect, device: device);
                            if (pushOp is null || CurrentEffect is not DragDropEffects.Move)
                                return;

                            pushOp.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName != nameof(FileSyncOperation.Status)
                                    || pushOp.Status is not FileOperation.OperationStatus.Completed)
                                    return;

                                // Once the second part is done, delete the file from the source device if needed, and notify if its another window
                                ShellFileOperation.SilentDelete(SourceDevice!, item.FullName);
                                if (IsDragFromMaster)
                                    IpcService.NotifyFileMoved(MasterPid, SourceDevice!, item);
                            };
                        };

                        Data.FileOpQ.AddOperation(pullOp);
                    }
                }
                // From archives, UNC paths, & DLNA servers
                else if (dataObject.GetDataPresent(AdbDataFormats.ShellidList))
                {
                    ShellFolder tempDrag = new(Data.RuntimeSettings.TempDragPath);
                    var shItems = ShellItemArray.FromDataObject((System.Runtime.InteropServices.ComTypes.IDataObject)dataObject);

                    ShellFileOperations shFileOp = new(NativeMethods.InterceptClipboard.MainWindowHandle);
                    shItems.ForEach(shia => shFileOp.QueueCopyOperation(shia, tempDrag));

                    ShellItem lastTopItem = null;
                    ShellItem lastTopSource = null;
                    shFileOp.PostCopyItem += (s, e) =>
                    {
                        // Skip non top level items
                        if (e.DestItem.Parent.ParsingName != Data.RuntimeSettings.TempDragPath)
                            return;

                        // A new top level item means the previous one is done
                        if (lastTopItem is not null && lastTopItem.ParsingName != e.DestItem.ParsingName)
                        {
                            if (isAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(device, [lastTopItem], App.AppDispatcher);
                            }
                            else
                                VerifyAndPush(targetFolder, new FileClass(lastTopItem), CurrentEffect, lastTopSource, device);
                        }

                        lastTopItem = e.DestItem;
                        lastTopSource = e.SourceItem;
                    };

                    shFileOp.FinishOperations += (s, e) =>
                    {
                        // The last item is not caught by the PostCopyItem event
                        if (lastTopItem is not null)
                        {
                            if (isAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(device, [lastTopItem], App.AppDispatcher);
                            }
                            else
                                VerifyAndPush(targetFolder, new FileClass(lastTopItem), CurrentEffect, lastTopSource, device);
                        }
                    };

                    shFileOp.PerformOperations();
                }
                // Was supposed to be the main method for zip archives, but Vanara covers that in ShellItemArray.
                // Will be left in to support any virtual files that don't provide ShellID List Array.
                else if (dataObject.GetDataPresent(AdbDataFormats.FileContents))
                {
                    Task.Run(() =>
                    {
                        string[] files = new string[Descriptors.Length];

                        for (int i = 0; i < Descriptors.Length; i++)
                        {
                            files[i] = FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, Descriptors[i].Name, '\\');
                            if (Descriptors[i].IsDirectory)
                                continue;

                            System.Runtime.InteropServices.ComTypes.IStream stream;
                            try
                            {
                                // Try to acquire the stream of each descriptor
                                stream = VirtualFileDataObject.GetFileContents(dataObject, i);
                            }
                            catch (COMException e)
                            {
                                // If failed, add a failed operation to the queue
                                App.SafeInvoke(() =>
                                {
                                    Data.FileOpQ.AddOperation(
                                        new FileSyncOperation(
                                            FileOperation.OperationType.Push,
                                            Descriptors[i],
                                            new(targetFolder),
                                            device,
                                            new FailedOpProgressViewModel(e.Message)));
                                });

                                continue;
                            }

                            // Save the stream to the temp folder, create the parent folder if it doesn't exist
                            Directory.CreateDirectory(FileHelper.GetParentPath(files[i]));

                            NativeMethods.SaveComStreamToFile(stream, files[i]);

                            var changeTimeUtc = Descriptors[i].ChangeTimeUtc;
                            if (changeTimeUtc is not null)
                                File.SetLastWriteTime(files[i], changeTimeUtc.Value.ToLocalTime());
                        }

                        IEnumerable<FileClass> shItems = [];
                        try
                        {
                            shItems = files
                                .Where(d => FileHelper.GetParentPath(d) == Data.RuntimeSettings.TempDragPath)
                                .Select(d => new FileClass(ShellItem.Open(d)));
                        }
                        catch
                        {
                        }
                        
                        if (shItems.Any())
                        {
                            if (isAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(device, shItems.Select(f => f.ShellItem).OfType<ShellItem>(), App.AppDispatcher);
                            }
                            else
                                VerifyAndPush(targetFolder, shItems, CurrentEffect, device);
                        }
                    });
                }
            }
            else if (IsWindows) // FileDrop format
            {
                if (isAppDrive)
                {
                    if (FileHelper.AllFilesAreApks(DragFiles))
                        ShellFileOperation.PushPackages(device, CurrentFiles.Select(f => f.ShellItem).OfType<ShellItem>(), App.AppDispatcher);
                }
                else
                    VerifyAndPush(targetFolder, CurrentFiles, CurrentEffect, device);
            }
            else if (SourceDevice?.ID == device.ID)
            {
                // Dragging a folder into itself is not allowed
                if (DragFiles.Length == 1 && DragFiles[0] == targetFolder && IsDrag)
                    return;

                if (isAppDrive)
                {
                    if (FileHelper.AllFilesAreApks(DragFiles))
                        ShellFileOperation.InstallPackages(device, CurrentFiles, App.AppDispatcher);
                }
                else
                {
                    var masterPid = IsDragFromMaster ? MasterPid : 0;
                    VerifyAndPaste(isLink ? DragDropEffects.Link : CurrentEffect,
                               targetFolder,
                               CurrentFiles,
                               App.AppDispatcher,
                               device,
                               Data.CurrentPath,
                               masterPid);
                }
            }
            else
            {
                // Not supported
                return;
            }

            if (CurrentEffect is DragDropEffects.Move)
                Clear();
        }

        ReadObject();

        if (IsDrag)
            ClearDrag();
    }

    public static async void VerifyAndPush(string targetPath, IEnumerable<ShellItem> pasteItems, LogicalDeviceViewModel? device = null)
    {
        device ??= Data.DevicesObject.Current;
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
    {
        device ??= Data.DevicesObject.Current;
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
        device ??= Data.DevicesObject.Current;
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

    public async void VerifyAndPaste(DragDropEffects cutType,
                               string targetPath,
                               IEnumerable<FileClass> pasteItems,
                               Dispatcher dispatcher,
                               LogicalDeviceViewModel device,
                               string currentPath,
                               int masterPid = 0)
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
                      dispatcher: dispatcher,
                      masterPid: masterPid);
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
                  cutType: cutType,
                  masterPid: masterPid);
    }

    public static SyncFile MergeFolderTree(FileClass folder, string targetPath, IEnumerable<string> filesToReplace)
    {
        StringComparer comparer = StringComparer.InvariantCultureIgnoreCase;

        var children = folder.Children;
        if (children is null || children.Length == 0)
            return folder.GetSyncFile();

        var parent = folder.ParentPath;

        var remote = Directory.EnumerateFiles(targetPath, "*", SearchOption.AllDirectories)
            .Select(f => FileHelper.ConcatPaths(parent, FileHelper.ExtractRelativePath(f, targetPath)))
            .ToHashSet(comparer);

        var filesToReplaceSet = filesToReplace.ToHashSet(comparer);

        var tree = children.Where(c => !remote.Contains(c.Name) || filesToReplaceSet.Contains(c.Name)).ToList();

        return new(folder, tree);
    }

    /// <summary>
    /// Check for existing items in the target location and resolve conflicts.
    /// Matching folders are merged; only nested file collisions are prompted.
    /// </summary>
    public static async Task<FileMergeHelper.MergeOutcome<string>> MergeFiles(IEnumerable<string> filePaths, string targetPath, LogicalDeviceViewModel? device = null)
    {
        if (filePaths is null || targetPath is null)
            return new([], EmptyPathSet, EmptyPathSet);

        var items = filePaths.ToList();
        if (items.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        device ??= Data.DevicesObject.Current;
        var sep = FileHelper.GetSeparator(targetPath);
        var caseSensitive = sep is '/' && DriveHelper.GetRestrictions(targetPath, device).CaseInsensitiveNames is not true;
        StringComparer comparer = caseSensitive
            ? StringComparer.InvariantCulture
            : StringComparer.InvariantCultureIgnoreCase;

        Dictionary<string, FileStat>? androidListing = null;
        var androidListingFailed = false;
        if (sep is '/' && device is not null && !IsExplorerListing(targetPath, device))
        {
            androidListing = FileMergeHelper.TryListAndroidDirByName(device.ID, targetPath, comparer);
            androidListingFailed = androidListing is null;
        }

        var candidates = items.Select(path =>
        {
            var name = FileHelper.GetFullName(path);
            var isWindowsSource = path.Contains('\\') || (path.Length >= 2 && path[1] == ':');

            bool isDir;
            long? size = null;
            DateTime? mtimeUtc = null;

            if (isWindowsSource)
            {
                isDir = Directory.Exists(path);
                if (!isDir)
                {
                    try
                    {
                        if (File.Exists(path))
                        {
                            var info = new FileInfo(path);
                            size = info.Length;
                            mtimeUtc = info.LastWriteTimeUtc;
                        }
                    }
                    catch
                    { }
                }
            }
            else
            {
                var src = Data.DirList?.FileList?.FirstOrDefault(f =>
                    comparer.Equals(f.FullPath, path) || comparer.Equals(f.FullName, name));
                isDir = src?.IsDirectory ?? false;
                if (!isDir)
                {
                    size = src?.Size;
                    mtimeUtc = src?.ModifiedTime?.ToUniversalTime();
                }
            }

            return new FileMergeHelper.ConflictCandidate(path, name, isDir, size, mtimeUtc);
        }).ToList();

        FileMergeHelper.DestEntry GetDest(string name) => sep is '/'
            ? FileMergeHelper.GetAndroidDestEntry(targetPath, name, androidListing)
            : FileMergeHelper.GetWindowsDestEntry(targetPath, name);

        HashSet<string> existingNames;
        if (androidListingFailed)
        {
            existingNames = candidates.Select(c => c.Name).ToHashSet(comparer);
        }
        else
        {
            existingNames = candidates
                .Select(c => c.Name)
                .Where(n => GetDest(n).Exists)
                .ToHashSet(comparer);
        }

        if (existingNames.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        var deviceId = device?.ID;
        var comparisons = await Task.Run(() => FileMergeHelper.ExpandConflicts(
            candidates.Where(c => existingNames.Contains(c.Name)),
            targetPath,
            GetDest,
            sep is '\\',
            deviceId,
            comparer));

        if (comparisons.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        var conflictNames = comparisons.Select(c => c.Name).ToHashSet(comparer);
        var sourcePath = GetCommonParentPath(items);
        var resolution = await PromptConflictResolution(
            sourcePath, targetPath, conflictNames.Count, comparisons);

        return ApplyPathMergeOutcome(items, candidates, GetDest, conflictNames, resolution, comparer);
    }

    public static Task<FileMergeHelper.MergeOutcome<FileClass>> MergeFiles(string targetPath, params IEnumerable<FileClass> filePaths)
        => MergeFiles(targetPath, filePaths, null);

    /// <summary>
    /// Check for existing items in the target location and resolve conflicts.
    /// Matching folders are merged; only nested file collisions are prompted.
    /// </summary>
    public static async Task<FileMergeHelper.MergeOutcome<FileClass>> MergeFiles(string targetPath, IEnumerable<FileClass> filePaths, LogicalDeviceViewModel? device)
    {
        if (filePaths is null || targetPath is null)
            return new([], EmptyPathSet, EmptyPathSet);

        var items = filePaths.ToList();
        if (items.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        device ??= Data.DevicesObject.Current;
        var sep = FileHelper.GetSeparator(targetPath);
        var caseSensitive = sep is '/' && DriveHelper.GetRestrictions(targetPath, device).CaseInsensitiveNames is not true;
        StringComparer comparer = caseSensitive
            ? StringComparer.InvariantCulture
            : StringComparer.InvariantCultureIgnoreCase;

        Dictionary<string, FileStat>? androidListing = null;
        var androidListingFailed = false;
        if (sep is '/' && device is not null && !IsExplorerListing(targetPath, device))
        {
            androidListing = FileMergeHelper.TryListAndroidDirByName(device.ID, targetPath, comparer);
            androidListingFailed = androidListing is null;
        }

        var candidates = items.Select(f =>
        {
            long? size = f.Size;
            DateTime? mtimeUtc = f.IsDirectory ? null : f.ModifiedTime?.ToUniversalTime();

            if (!f.IsDirectory
                && f.PathType is FilePathType.Windows
                && File.Exists(f.FullPath))
            {
                try
                {
                    var info = new FileInfo(f.FullPath);
                    size ??= info.Length;
                    mtimeUtc ??= info.LastWriteTimeUtc;
                }
                catch
                { }
            }

            return new FileMergeHelper.ConflictCandidate(f.FullPath, f.FullName, f.IsDirectory, size, mtimeUtc);
        }).ToList();

        FileMergeHelper.DestEntry GetDest(string name) => sep is '/'
            ? FileMergeHelper.GetAndroidDestEntry(targetPath, name, androidListing)
            : FileMergeHelper.GetWindowsDestEntry(targetPath, name);

        HashSet<string> existingNames;
        if (androidListingFailed)
        {
            existingNames = candidates.Select(c => c.Name).ToHashSet(comparer);
        }
        else
        {
            existingNames = candidates
                .Select(c => c.Name)
                .Where(n => GetDest(n).Exists)
                .ToHashSet(comparer);
        }

        if (existingNames.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        var deviceId = device?.ID;
        var comparisons = await Task.Run(() => FileMergeHelper.ExpandConflicts(
            candidates.Where(c => existingNames.Contains(c.Name)),
            targetPath,
            GetDest,
            sep is '\\',
            deviceId,
            comparer));

        if (comparisons.Count == 0)
            return new(items, EmptyPathSet, EmptyPathSet);

        var conflictNames = comparisons.Select(c => c.Name).ToHashSet(comparer);
        var sourcePath = GetCommonParentPath(items.Select(f => f.FullPath));
        var resolution = await PromptConflictResolution(
            sourcePath, targetPath, conflictNames.Count, comparisons);

        return ApplyFileClassMergeOutcome(items, GetDest, conflictNames, resolution, comparer);
    }

    private static bool IsExplorerListing(string targetPath, LogicalDeviceViewModel? device)
    {
        var current = Data.DevicesObject.Current;
        if (current is null)
            return false;
        if (device is not null && device.ID != current.ID)
            return false;

        return string.Equals(Data.CurrentPath, targetPath, StringComparison.Ordinal);
    }

    private static readonly HashSet<string> EmptyPathSet = [];

    private static string GetCommonParentPath(IEnumerable<string> fullPaths)
    {
        var parents = fullPaths
            .Where(p => !string.IsNullOrEmpty(p))
            .Select(p => FileHelper.GetParentPath(p))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        return parents.Count == 0 ? string.Empty : parents[0];
    }

    private static async Task<(FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames)> PromptConflictResolution(
        string sourcePath,
        string targetPath,
        int conflictCount,
        IReadOnlyList<FileMergeHelper.ConflictComparisonInfo> comparisons)
    {
        string destination = FileHelper.GetFullName(targetPath);
        if (Data.CurrentDisplayNames.TryGetValue(targetPath, out var drive))
            destination = drive;

        var message = conflictCount == 1
            ? string.Format(Strings.Resources.S_CONFLICT_ITEMS_DESTINATION, destination)
            : string.Format(Strings.Resources.S_CONFLICT_ITEMS_PLURAL_DESTINATION, conflictCount, destination);

        var choice = await DialogService.ShowConflictResolution(message, Strings.Resources.S_PASTE_CONFLICTS_TITLE);

        if (choice is FileMergeHelper.ConflictResolution.PerFile)
        {
            var replaceNames = await DialogService.ShowPerFileConflictResolution(
                comparisons,
                string.Format(Strings.Resources.S_CONFLICT_DECIDE_TITLE, comparisons.Count),
                sourcePath,
                targetPath);

            if (replaceNames is null)
                return (FileMergeHelper.ConflictResolution.Cancel, null);

            return (FileMergeHelper.ConflictResolution.PerFile, replaceNames);
        }

        return (choice, null);
    }

    private static FileMergeHelper.MergeOutcome<string> ApplyPathMergeOutcome(
        List<string> items,
        List<FileMergeHelper.ConflictCandidate> candidates,
        Func<string, FileMergeHelper.DestEntry> getDest,
        HashSet<string> conflictRelativePaths,
        (FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames) resolution,
        StringComparer comparer)
    {
        if (resolution.Resolution is FileMergeHelper.ConflictResolution.Cancel)
            return new([], EmptyPathSet, EmptyPathSet);

        var replaceSet = ResolveReplaceSet(conflictRelativePaths, resolution, comparer);
        var byName = candidates.ToDictionary(c => c.Name, comparer);

        var kept = items.Where(p =>
        {
            var name = FileHelper.GetFullName(p);
            var dest = getDest(name);
            if (!dest.Exists)
                return true;

            if (byName.TryGetValue(name, out var candidate) && candidate.IsDirectory && dest.IsDirectory)
                return true; // merge folder

            return replaceSet.Contains(name);
        }).ToList();

        return new(kept, replaceSet, conflictRelativePaths);
    }

    private static FileMergeHelper.MergeOutcome<FileClass> ApplyFileClassMergeOutcome(
        List<FileClass> items,
        Func<string, FileMergeHelper.DestEntry> getDest,
        HashSet<string> conflictRelativePaths,
        (FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames) resolution,
        StringComparer comparer)
    {
        if (resolution.Resolution is FileMergeHelper.ConflictResolution.Cancel)
            return new([], EmptyPathSet, EmptyPathSet);

        var replaceSet = ResolveReplaceSet(conflictRelativePaths, resolution, comparer);

        var kept = items.Where(f =>
        {
            var dest = getDest(f.FullName);
            if (!dest.Exists)
                return true;

            if (f.IsDirectory && dest.IsDirectory)
                return true; // merge folder

            return replaceSet.Contains(f.FullName);
        }).ToList();

        return new(kept, replaceSet, conflictRelativePaths);
    }

    private static HashSet<string> ResolveReplaceSet(
        HashSet<string> conflictRelativePaths,
        (FileMergeHelper.ConflictResolution Resolution, IReadOnlyList<string>? ReplaceNames) resolution,
        StringComparer comparer)
        => resolution.Resolution switch
        {
            FileMergeHelper.ConflictResolution.Replace => conflictRelativePaths,
            FileMergeHelper.ConflictResolution.SkipConflicts => new HashSet<string>(comparer),
            FileMergeHelper.ConflictResolution.PerFile =>
                resolution.ReplaceNames?.ToHashSet(comparer) ?? new HashSet<string>(comparer),
            _ => new HashSet<string>(comparer),
        };

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

    public static void ClearTempFolder()
    {
        try
        {
            Directory.Delete(Data.RuntimeSettings.TempDragPath, true);
        }
        catch
        { }

        Directory.CreateDirectory(Data.RuntimeSettings.TempDragPath);

        // Drop leftover archive extract staging from a previous clipboard/drag that was never pulled.
        ArchiveExtract.BeginCleanupAllStaging();
    }
}
