using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services.AppInfra;
using ADB_Explorer.ViewModels;
using Vanara.Windows.Shell;
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

    private DragDropEffects pasteState = DragDropEffects.None;
    public DragDropEffects PasteState
    {
        get => pasteState;
        set
        {
            if (Set(ref pasteState, value))
            {
                Data.FileActions.IsCutState.Value = value is DragDropEffects.Move;
                Data.FileActions.IsCopyState.Value = value is DragDropEffects.Copy;
            }
        }
    }

    private DragDropEffects dropEffect = DragDropEffects.None;
    public DragDropEffects DropEffect
    {
        get => dropEffect;
        set => Set(ref dropEffect, value);
    }

    private DragDropEffects currentDropEffect = DragDropEffects.None;
    public DragDropEffects CurrentDropEffect
    {
        get => currentDropEffect;
        set => Set(ref currentDropEffect, value);
    }

    private string dropTarget = null;
    public string DropTarget
    {
        get => dropTarget;
        set => Set(ref dropTarget, value);
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

    public enum DragState
    {
        None,
        Pending,
        Active,
    }

    private DragState dragStatus = DragState.None;

    public DragState DragStatus
    {
        get => dragStatus;
        set => Set(ref dragStatus, value);
    }

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

    public NativeMethods.HResult DragResult { get; set; }

    public DragDropEffects CurrentEffect => IsDrag ? DropEffect : PasteState;
    public string CurrentParent => IsDrag ? DragParent : ParentFolder;
    public bool IsSelf => CurrentSource.HasFlag(DataSource.Self);
    public bool IsSelfClipboard => IsSelf && IsClipboard;
    public bool IsWindows => !CurrentSource.HasFlag(DataSource.None) && !CurrentSource.HasFlag(DataSource.Android);
    public bool IsVirtual => CurrentSource.HasFlag(DataSource.Virtual);

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
        set
        {
            // The Set method only compares instances
            if (dragFiles.SequenceEqual(value))
                return;

            Set(ref dragFiles, value);
            _currentFiles = null;
        }
    }

    private FileDescriptor[] descriptors = [];
    public FileDescriptor[] Descriptors
    {
        get => descriptors;
        set
        {
            // The Set method only compares instances
            if (descriptors.SequenceEqual(value))
                return;

            Set(ref descriptors, value);
            _currentFiles = null;
        }
    }

    private IEnumerable<FileClass> _currentFiles = [];
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

    public LogicalDeviceViewModel SourceDevice { get; private set; }

    public static string UserTemp => $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\Temp\\";

    public void UpdateUI()
    {
        FileActionLogic.UpdateFileActions();

        List<FileClass> cutItems = [];
        if (PasteSource is not DataSource.None && PasteSource.HasFlag(DataSource.Self))
            cutItems = [.. Data.DirList.FileList.Where(f => Files.Contains(f.FullPath))];

        cutItems.ForEach(file => file.CutState = PasteState);
        Data.DirList?.FileList.Except(cutItems).ForEach(file => file.CutState = DragDropEffects.None);
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
        }

        ClearDrag();
        UpdateUI();
    }

    public void ClearDrag()
    {
        if (!IsDrag)
            return;

        Data.RuntimeSettings.DragBitmap = null;
        if (IsClipboard)
            return;

        DropEffect = DragDropEffects.None;
        DragPasteSource = DataSource.None;
        DragFiles = [];
        DragParent = "";
    }

    public void GetClipboardPasteItems()
    {
        var CPDO = Clipboard.GetDataObject();

#if !DEPLOY
        DebugLog.PrintLine($"Clipboard formats: {string.Join(", ", CPDO.GetFormats())}");
#endif

        var allowedEffect = GetAllowedDragEffects(CPDO);
        if (allowedEffect is DragDropEffects.None)
        {
            PasteState = DragDropEffects.None;
            PasteSource = DataSource.None;
            Files = [];
            _currentFiles = [];

            UpdateUI();
            return;
        }

        var prefDropEffect = VirtualFileDataObject.GetPreferredDropEffect(CPDO);

        // Link is only allowed depending on the target
        if (prefDropEffect.HasFlag(DragDropEffects.Copy) && allowedEffect.HasFlag(DragDropEffects.Copy))
            PasteState = DragDropEffects.Copy;
        else if (prefDropEffect.HasFlag(DragDropEffects.Move) && allowedEffect.HasFlag(DragDropEffects.Move))
            PasteState = DragDropEffects.Move;
        else if (prefDropEffect is DragDropEffects.Move && allowedEffect is DragDropEffects.Copy)
            PasteState = DragDropEffects.Copy; // fallback to copy
        else
            PasteState = DragDropEffects.None;

        Files = DragFiles;
        ParentFolder = DragParent;

        UpdateUI();
    }

    public void UpdateSelfVFDO(bool isDrag)
    {
        if (VirtualFileDataObject.SelfFiles is null || !VirtualFileDataObject.SelfFiles.Any())
            return;

        if (isDrag)
        {
            DragPasteSource |= DataSource.Android | DataSource.Self;
        }
        else
        {
            PasteSource |= DataSource.Android | DataSource.Self;
        }

        SourceDevice = Data.CurrentADBDevice.Device;
        MasterPid = Environment.ProcessId;
        DragParent = VirtualFileDataObject.SelfFiles.First().ParentPath;
        DragFiles = [.. VirtualFileDataObject.SelfFileGroup.FileDescriptors.Select(d => d.Name)];
        Descriptors = [.. VirtualFileDataObject.SelfFileGroup.FileDescriptors];

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
            if (FileHelper.AllFilesAreApks(DragFiles))
                return DragDropEffects.Copy;
        }
        else if (dataContext is null || file?.IsDirectory is true)
        {
            Data.CopyPaste.DropTarget = dataContext is null
                ? Data.CurrentPath
                : file.FullPath;

            if (CurrentSource.HasFlag(DataSource.Android))
            {
                if (IsDrag)
                    return FileActionLogic.EnableDropPaste(file);
            }
            else if (CurrentSource.HasFlag(DataSource.Virtual))
                return DragDropEffects.Copy;
            
            return DragDropEffects.Move | DragDropEffects.Copy;
        }

        return DragDropEffects.None;
    }

    public void PreviewDataObject(IDataObject dataObject)
    {
        CurrentSource &= ~(DataSource.Android | DataSource.Self | DataSource.Virtual);

        if (Data.CurrentADBDevice is null)
            return;

        DragParent = "";
        string[] oldFiles = [.. DragFiles];

        // ADB Drop - for all Android to Android transfers (including self)
        if (dataObject.GetDataPresent(AdbDataFormats.AdbDrop) && dataObject.GetData(AdbDataFormats.AdbDrop) is MemoryStream adbStream)
        {
            var dragList = NativeMethods.ADBDRAGLIST.FromStream(adbStream);
            var deviceId = dragList.deviceId;

            var device = Data.DevicesObject.UIList.OfType<LogicalDeviceViewModel>().FirstOrDefault(d => d.ID == deviceId && d.Status is AbstractDevice.DeviceStatus.Ok);
            if (!IsDrag && device is null)
            {
                Clear();
                return;
            }
            else
                SourceDevice = device;

            MasterPid = dragList.pid;
            DragParent = dragList.parentFolder;
            DragFiles = [.. dragList.items.Select(f => FileHelper.ConcatPaths(DragParent, f))];

            CurrentSource |= DataSource.Android;
            if (deviceId == Data.CurrentADBDevice.ID)
                CurrentSource |= DataSource.Self;
            else
                CurrentSource |= DataSource.Virtual;

            if (dataObject.GetDataPresent(AdbDataFormats.FileDescriptor))
            {
                Task.Run(() =>
                {
                    Task.Delay(500);
                    App.Current.Dispatcher.Invoke(() => GetDescriptors(dataObject));
                });
            }
        }
        // Shell ID List - the only format Microsoft supports for anything added after Windows XP (non-ZIP archives, UNC paths, etc.)
        else if (dataObject.GetDataPresent(AdbDataFormats.ShellidList))
        {
            var shItems = ShellItemArray.FromDataObject((System.Runtime.InteropServices.ComTypes.IDataObject)dataObject);
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
            GetDescriptors(dataObject);

            DragFiles = [.. Descriptors.Where(d => !d.Name.Contains('\\')).Select(d => d.Name)];

            CurrentSource |= DataSource.Virtual;
            if (dataObject.GetDataPresent(AdbDataFormats.FileContents))
                CurrentSource &= ~DataSource.Android;
        }
        // If the data object only has FileDrop, then it's probably dropping by target detect, which we can't support (7-Zip, WinRAR, etc.)
        else
        {
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

    public void AcceptDataObject(System.Windows.DragEventArgs e, FrameworkElement sender, bool isLink = false)
    {
        var dataContext = sender.DataContext;

        string targetFolder = dataContext is FileClass { IsDirectory: true } file
            ? file.FullPath
            : Data.CurrentPath;
        
        // Do not perform implicit duplicate by drag (only with Ctrl)
        if (IsSelf && targetFolder == DragParent && e.KeyStates is DragDropKeyStates.None)
            return;

        AcceptDataObject(e.Data, targetFolder, isLink);
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
            // For all cases where the files aren't immediately available on disk
            if (IsVirtual)
            {
                ClearTempFolder();

                // Transfer from another Android device
                if (!IsWindows)
                {
                    ADBService.AdbDevice sourceDevice = new(SourceDevice);
                    foreach (var item in CurrentFiles)
                    {
                        SyncFile target = new(item) { PathType = FilePathType.Windows };
                        target.UpdatePath(FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, item.FullName, '\\'));

                        // Pull the file from the source device to the temp folder
                        var pullOp = FileSyncOperation.PullFile(new(item), target, sourceDevice, App.Current.Dispatcher);
                        pullOp.PropertyChanged += (s, e) =>
                        {
                            if (e.PropertyName != nameof(FileSyncOperation.Status)
                                || pullOp.Status is not FileOperation.OperationStatus.Completed)
                                return;

                            // Once done, create a shell item and push it to the target device (current)
                            FileClass file = new(target) { ShellItem = ShellItem.Open(target.FullPath) };
                            if (Data.FileActions.IsAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(Data.CurrentADBDevice, [file.ShellItem], App.Current.Dispatcher);

                                return;
                            }

                            var pushOp = VerifyAndPush(targetFolder, file, CurrentEffect);
                            if (pushOp is not null || CurrentEffect is not DragDropEffects.Move)
                                return;

                            pushOp.PropertyChanged += (s, e) =>
                            {
                                if (e.PropertyName != nameof(FileSyncOperation.Status)
                                    || pushOp.Status is not FileOperation.OperationStatus.Completed)
                                    return;

                                // Once the second part is done, delete the file from the source device if needed, and notify if its another window
                                ShellFileOperation.SilentDelete(sourceDevice, item.FullName);
                                if (IsDragFromMaster)
                                    IpcService.NotifyFileMoved(MasterPid, sourceDevice, item);
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
                            if (Data.FileActions.IsAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(Data.CurrentADBDevice, [lastTopItem], App.Current.Dispatcher);
                            }
                            else
                                VerifyAndPush(targetFolder, new FileClass(lastTopItem), CurrentEffect, lastTopSource);
                        }

                        lastTopItem = e.DestItem;
                        lastTopSource = e.SourceItem;
                    };

                    shFileOp.FinishOperations += (s, e) =>
                    {
                        // The last item is not caught by the PostCopyItem event
                        if (lastTopItem is not null)
                        {
                            if (Data.FileActions.IsAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(Data.CurrentADBDevice, [lastTopItem], App.Current.Dispatcher);
                            }
                            else
                                VerifyAndPush(targetFolder, new FileClass(lastTopItem), CurrentEffect, lastTopSource);
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
                                App.Current.Dispatcher.Invoke(() =>
                                {
                                    Data.FileOpQ.AddOperation(
                                        new FileSyncOperation(
                                            FileOperation.OperationType.Push,
                                            Descriptors[i],
                                            new(targetFolder),
                                            Data.CurrentADBDevice,
                                            new FailedOpProgressViewModel(e.Message)));
                                });

                                continue;
                            }

                            // Save the stream to the temp folder, create the parent folder if it doesn't exist
                            Directory.CreateDirectory(FileHelper.GetParentPath(files[i]));

                            NativeMethods.SaveComStreamToFile(stream, files[i]);
                            if (Descriptors[i].ChangeTimeUtc is not null)
                                File.SetLastWriteTime(files[i], Descriptors[i].ChangeTimeUtc.Value.ToLocalTime());
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
                            if (Data.FileActions.IsAppDrive)
                            {
                                if (FileHelper.AllFilesAreApks(DragFiles))
                                    ShellFileOperation.PushPackages(Data.CurrentADBDevice, shItems.Select(f => f.ShellItem), App.Current.Dispatcher);
                            }
                            else
                                VerifyAndPush(targetFolder, shItems, CurrentEffect);
                        }
                    });
                }
            }
            else if (IsWindows) // FileDrop format
            {
                if (Data.FileActions.IsAppDrive)
                {
                    if (FileHelper.AllFilesAreApks(DragFiles))
                        ShellFileOperation.PushPackages(Data.CurrentADBDevice, CurrentFiles.Select(f => f.ShellItem), App.Current.Dispatcher);
                }
                else
                    VerifyAndPush(targetFolder, CurrentFiles, CurrentEffect);
            }
            else if (IsSelf)
            {
                // Dragging a folder into itself is not allowed
                if (DragFiles.Length == 1 && DragFiles[0] == targetFolder && IsDrag)
                    return;

                if (Data.FileActions.IsAppDrive)
                {
                    if (FileHelper.AllFilesAreApks(DragFiles))
                        ShellFileOperation.InstallPackages(Data.CurrentADBDevice, CurrentFiles, App.Current.Dispatcher);
                }
                else
                {
                    var masterPid = IsDragFromMaster ? MasterPid : 0;
                    VerifyAndPaste(isLink ? DragDropEffects.Link : CurrentEffect,
                               targetFolder,
                               CurrentFiles,
                               App.Current.Dispatcher,
                               Data.CurrentADBDevice,
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

    public static async void VerifyAndPush(string targetPath, IEnumerable<ShellItem> pasteItems)
    {
        var files = await MergeFiles(pasteItems.Select(f => f.ParsingName), targetPath);
        if (!files.Any())
            return;

        if (files.Count() < pasteItems.Count())
        {
            pasteItems = pasteItems.Where(f => files.Contains(f.ParsingName));
        }

        FileActionLogic.PushShellObjects(pasteItems, targetPath);
    }

    public static async void VerifyAndPush(string targetPath, IEnumerable<FileClass> pasteItems, DragDropEffects dropEffects = DragDropEffects.Copy)
    {
        pasteItems = await MergeFiles(targetPath, pasteItems);
        if (!pasteItems.Any())
            return;

        FileActionLogic.PushShellObjects(pasteItems.Select(f => f.ShellItem), targetPath, dropEffects);
    }

    public static FileSyncOperation VerifyAndPush(string targetPath, FileClass pasteItem, DragDropEffects dropEffects = DragDropEffects.Copy, ShellItem originalShellItem = null)
    {
        var items = MergeFiles(targetPath, pasteItem).Result;
        if (!items.Any())
            return null;

        return FileActionLogic.PushShellObject(pasteItem.ShellItem, targetPath, dropEffects, originalShellItem);
    }

    public async void VerifyAndPaste(DragDropEffects cutType,
                               string targetPath,
                               IEnumerable<FileClass> pasteItems,
                               Dispatcher dispatcher,
                               ADBService.AdbDevice device,
                               string currentPath,
                               int masterPid = 0)
    {
        pasteItems = await RemoveAncestor(pasteItems, targetPath, cutType);
        if (!pasteItems.Any())
            return;

        pasteItems = await MergeFiles(targetPath, pasteItems);
        if (!pasteItems.Any())
            return;

        ShellFileOperation.MoveItems(device: device,
                  items: pasteItems,
                  targetPath: targetPath,
                  currentPath: currentPath,
                  existingItems: Data.DirList.FileList.Select(f => f.FullName),
                  dispatcher: dispatcher,
                  cutType: cutType,
                  masterPid: masterPid);
    }

    /// <summary>
    /// Check for existing top level items in the target location. <br />
    /// Ask the user whether to abort, continue, or exclude the conflicting items.
    /// </summary>
    /// <param name="filePaths">Full paths of the files to be transferred.</param>
    /// <param name="targetPath">Full path of the target location.</param>
    /// <returns>
    /// An empty list if user selected Cancel. <br />
    /// The original list if user selected Merge or Replace. <br />
    /// The file list excluding the top level conflicting items if user selected Skip.
    /// </returns>
    public static async Task<IEnumerable<string>> MergeFiles(IEnumerable<string> filePaths, string targetPath)
    {
        if (filePaths is null || targetPath is null)
            return [];

        // Figure out whether the target is Windows or Android
        var sep = FileHelper.GetSeparator(targetPath);

        // File names on (non virtual) Unix file systems are case sensitive
        var isUnix = sep is '/' && !DriveHelper.GetCurrentDrive(targetPath).IsFUSE;
        StringComparer comparer = isUnix
            ? StringComparer.InvariantCulture
            : StringComparer.InvariantCultureIgnoreCase;

        // Prepare a set with file system dependent comparison. Currently we only check for top level conflicts.
        // We receive full paths of the top level items in AdbDragList and FileDrop.

        // TODO: FileGroupDescriptor gives full hierarchy, so GetFullName is not good

        HashSet<string> fileNames = new(filePaths.Select(FileHelper.GetFullName), comparer);
        HashSet<string> existingItems;

        if (sep is '/') // Android
        {
            if (targetPath == Data.CurrentPath)
            {
                existingItems = Data.DirList.FileList.Select(f => f.FullPath).Intersect(fileNames).ToHashSet(comparer);
            }
            else
            {
                var foundFiles = ADBService.FindFilesInPath(Data.CurrentADBDevice.ID, targetPath, includeNames: fileNames, caseSensitive: isUnix);
                existingItems = foundFiles.Select(FileHelper.GetFullName).ToHashSet(comparer);
            }
        }
        else // Windows
        {
            var files = Directory.GetFiles(targetPath);
            var dirs = Directory.GetDirectories(targetPath);

            existingItems = dirs.Concat(files).Select(Path.GetFileName).Intersect(fileNames).ToHashSet(comparer);
        }

        var count = existingItems.Count;
        if (count < 1)
            return filePaths;

        string destination = FileHelper.GetFullName(targetPath);
        if (Data.CurrentDisplayNames.TryGetValue(targetPath, out var drive))
            destination = drive;

        var message = count == 1
            ? string.Format(Strings.Resources.S_CONFLICT_ITEMS_DESTINATION, destination)
            : string.Format(Strings.Resources.S_CONFLICT_ITEMS_PLURAL_DESTINATION, count, destination);

        var result = await DialogService.ShowConfirmation(
            message,
            Strings.Resources.S_PASTE_CONFLICTS_TITLE,
            primaryText: Strings.Resources.S_MERGE_OR_REPLACE,
            secondaryText: count == filePaths.Count() ? "" : Strings.Resources.S_SKIP,
            cancelText: Strings.Resources.S_CANCEL,
            icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 is ContentDialogResult.None) // Cancel
        {
            return [];
        }
        if (result.Item1 is ContentDialogResult.Secondary) // Skip
        {
            filePaths = [.. filePaths.Where(item => !existingItems.Contains(FileHelper.GetFullName(item)))];
        }

        return filePaths;
    }

    /// <summary>
    /// Check for existing top level items in the target location. <br />
    /// Ask the user whether to abort, continue, or exclude the conflicting items.
    /// </summary>
    /// <param name="targetPath">Full path of the target location.</param>
    /// <param name="filePaths">Full paths of the files to be transferred.</param>
    /// <returns>
    /// An empty list if user selected Cancel. <br />
    /// The original list if user selected Merge or Replace. <br />
    /// The file list excluding the top level conflicting items if user selected Skip.
    /// </returns>
    public static async Task<IEnumerable<FileClass>> MergeFiles(string targetPath, params IEnumerable<FileClass> filePaths)
    {
        if (filePaths is null || targetPath is null)
            return [];

        // Figure out whether the target is Windows or Android
        var sep = FileHelper.GetSeparator(targetPath);

        // File names on (non virtual) Unix file systems are case sensitive
        var isUnix = sep is '/' && !DriveHelper.GetCurrentDrive(targetPath).IsFUSE;
        StringComparer comparer = isUnix
            ? StringComparer.InvariantCulture
            : StringComparer.InvariantCultureIgnoreCase;

        // Prepare a set with file system dependent comparison. Currently we only check for top level conflicts.
        // We receive full paths of the top level items in AdbDragList and FileDrop.

        // TODO: FileGroupDescriptor gives full hierarchy, so GetFullName is not good

        HashSet<string> fileNames = new(filePaths.Select(f => f.FullName), comparer);
        HashSet<string> existingItems;

        if (sep is '/') // Android
        {
            if (targetPath == Data.CurrentPath)
            {
                existingItems = Data.DirList.FileList.Select(f => f.FullPath).Intersect(fileNames).ToHashSet(comparer);
            }
            else
            {
                var foundFiles = ADBService.FindFilesInPath(Data.CurrentADBDevice.ID, targetPath, includeNames: fileNames, caseSensitive: isUnix);
                existingItems = foundFiles.Select(FileHelper.GetFullName).ToHashSet(comparer);
            }
        }
        else // Windows
        {
            var files = Directory.GetFiles(targetPath);
            var dirs = Directory.GetDirectories(targetPath);

            existingItems = dirs.Concat(files).Select(Path.GetFileName).Intersect(fileNames).ToHashSet(comparer);
        }

        var count = existingItems.Count;
        if (count <= 0)
            return filePaths;

        string destination = FileHelper.GetFullName(targetPath);
        if (Data.CurrentDisplayNames.TryGetValue(targetPath, out var drive))
            destination = drive;

        var message = count == 1
            ? string.Format(Strings.Resources.S_CONFLICT_ITEMS_DESTINATION, destination)
            : string.Format(Strings.Resources.S_CONFLICT_ITEMS_PLURAL_DESTINATION, count, destination);

        var result = await DialogService.ShowConfirmation(
            message,
            Strings.Resources.S_PASTE_CONFLICTS_TITLE,
            primaryText: Strings.Resources.S_MERGE_OR_REPLACE,
            secondaryText: count == filePaths.Count() ? "" : Strings.Resources.S_SKIP,
            cancelText: Strings.Resources.S_CANCEL,
            icon: DialogService.DialogIcon.Exclamation);

        if (result.Item1 is ContentDialogResult.None) // Cancel
        {
            return [];
        }
        if (result.Item1 is ContentDialogResult.Secondary) // Skip
        {
            filePaths = [.. filePaths.Where(item => !existingItems.Contains(item.FullName))];
        }

        return filePaths;
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

        return result.Item1 is ContentDialogResult.Primary
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
    }
}
