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
            if (FieldHelper.TrySet(ref field, value))
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

    public LogicalDeviceViewModel? DropTargetDevice { get; set; }

    public string DropTargetName
    {
        get
        {
            if (DropTarget is null)
                return "";

            string destination = FileHelper.GetFullName(DropTarget);
            if (Data.CurrentDisplayNames.TryGetValue((DropTargetDevice?.ID, DropTarget), out var drive))
                destination = drive;

            return destination;
        }
    }

    public DataSource PasteSource
    {
        get;
        set
        {
            if (FieldHelper.TrySet(ref field, value)
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
            if (FieldHelper.TrySet(ref field, value)
                && field.HasFlag(DataSource.None)
                && value is not DataSource.None)
            {
                // Remove the none flag when settings something else
                field &= ~DataSource.None;
            }
        }
    } = DataSource.None;

    public DragState DragStatus { get; set; } = DragState.None;

    public bool IsDrag => DragPasteSource is not DataSource.None;
    public bool IsClipboard => PasteSource is not DataSource.None && !IsDrag;
    public bool HasFiles => PasteSource is not DataSource.None && Files.Length > 0;
    
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

    /// <summary>True while a tab header is being dragged, which shows <see cref="DragTabTooltip"/> instead of a file transfer's.</summary>
    [ObservableProperty]
    public partial bool IsTabDrag { get; set; } = false;

    /// <summary>The tab whose header the drag window shows in place of an image while <see cref="IsTabDrag"/>.</summary>
    [ObservableProperty]
    public partial ExplorerInstance? DragTab { get; set; }

    public string? DragTabTooltip { get; set; }

    /// <summary>
    /// The drag image's bounds in pixels relative to the cursor - so its location is negative when the
    /// mouse holds it away from its top left - else null for the standard size, above the cursor.
    /// </summary>
    public Rect? DragImageRectPx { get; set; }

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

    /// <summary>The data comes from an Android device of this app, which is the only app that provides it.</summary>
    public bool IsAndroid => CurrentSource.HasFlag(DataSource.Android);

    /// <summary>The data comes from the device the drop or paste is aimed at.</summary>
    public bool IsSelf => IsFromDevice(IsDrag ? DropTargetDevice : Data.Active.Device ?? Data.ActiveDevice);

    public bool IsSelfClipboard => IsSelf && IsClipboard;
    public bool IsWindows => !CurrentSource.HasFlag(DataSource.None) && !CurrentSource.HasFlag(DataSource.Android);
    public bool IsVirtual => CurrentSource.HasFlag(DataSource.Virtual);

    public string ParentFolder { get; set; } = "";

    public string DragParent { get; set; } = "";

    public string[] Files { get; set; } = [];

    /// <summary>
    /// True when the clipboard holds an image and no files, as last seen by <see cref="GetClipboardPasteItems"/>.
    /// Cached, not live-queried - polling the OS clipboard from a hot path (e.g. CanExecute) can stall/deadlock it.
    /// </summary>
    public bool HasClipboardImage { get; private set; }

    public string[] DragFiles
    {
        get;
        set
        {
            // SetProperty only compares instances
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
            // SetProperty only compares instances
            if (field.SequenceEqual(value))
                return;

            field = value;
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
                // Skip files removed from disk, or on a drive that's gone (ejected/disconnected),
                // after being copied but before paste.
                ShellItem item;
                try
                {
                    item = ShellItem.Open(file);
                }
                catch (Exception ex) when (ex is FileNotFoundException or Win32Exception)
                {
                    continue;
                }

                yield return new(item);
            }
        }
        else
        {
            if (IsAndroid && VirtualFileDataObject.SelfFiles is not null)
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

    public LogicalDeviceViewModel? SourceDevice { get; private set; }

    public static string UserTemp => $"{Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)}\\Temp\\";

    public void UpdateUI()
    {
        FileActionLogic.UpdateFileActions();

        var listing = Data.Files.DirList?.FileList;
        if (listing is not null)
        {
            List<FileClass> cutItems = [];
            var listingDevice = Data.Files.Device ?? Data.ActiveDevice;
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
        => SourceDevice is not null && device is not null && SourceDevice.ID == device.ID;

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

    /// <summary>
    /// OS clipboard sequence number right after this app's own self-copy wrote to the clipboard -
    /// used by <see cref="ShouldKeepSelfAndroidClipboard"/> to bound how long it tolerates a
    /// transient missing-AdbDrop read to just that one write. Set via <see cref="MarkSelfClipboardWritten"/>.
    /// </summary>
    private uint? _selfCopyClipboardSequence;

    public void MarkSelfClipboardWritten() => _selfCopyClipboardSequence = NativeMethods.MGetClipboardSequenceNumber();

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
