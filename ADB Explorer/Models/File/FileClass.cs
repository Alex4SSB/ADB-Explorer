using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Models;

public class FileClass : FilePath, IFileStat, IBrowserItem
{
    #region Notify Properties

    private ulong? size;
    public ulong? Size
    {
        get => size;
        set => Set(ref size, value);
    }

    private bool isLink;
    public bool IsLink
    {
        get => isLink;
        set
        {
            if (Set(ref isLink, value))
                UpdateSpecialType();
        }
    }

    private string linkTarget = "";
    public string LinkTarget
    {
        get => linkTarget;
        set => Set(ref linkTarget, value);
    }

    private FileType type;
    public FileType Type
    {
        get => type;
        set
        {
            if (Set(ref type, value))
                UpdateSpecialType();
        }
    }

    private string typeName;
    public string TypeName
    {
        get => typeName;
        private set => Set(ref typeName, value);
    }

    private DateTime? modifiedTime;
    public DateTime? ModifiedTime
    {
        get => modifiedTime;
        set
        {
            if (Set(ref modifiedTime, value))
                OnPropertyChanged(nameof(ModifiedTimeString));
        }
    }

    private BitmapSource icon = null;
    public BitmapSource Icon
    {
        get => icon;
        private set => Set(ref icon, value);
    }

    private BitmapSource iconOverlay = null;
    public BitmapSource IconOverlay
    {
        get => iconOverlay;
        private set => Set(ref iconOverlay, value);
    }

    private DragDropEffects cutState = DragDropEffects.None;
    public DragDropEffects CutState
    {
        get => cutState;
        set => Set(ref cutState, value);
    }

    private TrashIndexer trashIndex;
    public TrashIndexer TrashIndex
    {
        get => trashIndex;
        set
        {
            Set(ref trashIndex, value);
            if (value is not null && value.OriginalPath is not null)
                FullName = FileHelper.GetFullName(value.OriginalPath);
        }
    }

    #endregion

    private bool extensionIsGlyph = false;
    public bool ExtensionIsGlyph
    {
        get => extensionIsGlyph;
        set => Set(ref extensionIsGlyph, value);
    }

    private bool extensionIsFontIcon = false;
    public bool ExtensionIsFontIcon
    {
        get => extensionIsFontIcon;
        set => Set(ref extensionIsFontIcon, value);
    }

    public bool IsTemp { get; set; }

    public FileNameSort SortName { get; private set; }

    public string[] Children
    {
        get
        {
            if (!IsDirectory)
                return null;

            var findCmd = "find";
            if (Enum.TryParse<ShellCommands.ShellCmd>(findCmd, out var enumCmd)
                && ShellCommands.DeviceCommands.TryGetValue(Data.CurrentADBDevice.ID, out var dict)
                && dict.TryGetValue(enumCmd, out var deviceCmd))
            {
                findCmd = deviceCmd;
            }

            var target = ADBService.EscapeAdbShellString(FullName);
            string[] args = [ParentPath, "&&", findCmd, target, "-type f", "&&", findCmd, target, "-mindepth 1 -type d -empty -printf '%p/\\n'"];

            ADBService.ExecuteDeviceAdbShellCommand(Data.CurrentADBDevice.ID, "cd", out string stdout, out _, CancellationToken.None, args);

            return stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
        }
    }

    public IEnumerable<FileDescriptor> Descriptors { get; private set; }

    #region Read Only Properties

    public bool IsApk => AdbExplorerConst.APK_NAMES.Contains(Extension.ToUpper());

    public bool IsInstallApk => Array.IndexOf(AdbExplorerConst.INSTALL_APK, Extension.ToUpper()) > -1;

    /// <summary>
    /// Returns the extension (including the period ".") of a regular file.<br />
    /// Returns an empty string if file has no extension, or is not a regular file.
    /// </summary>
    public override string Extension => Type is FileType.File ? base.Extension : "";

    public string ShortExtension
    {
        get
        {
            return (Extension.Length > 1 && Array.IndexOf(AdbExplorerConst.UNICODE_ICONS, char.GetUnicodeCategory(Extension[1])) > -1)
                ? Extension[1..]
                : "";
        }
    }

    public string ModifiedTimeString => ModifiedTime?.ToString(CultureInfo.CurrentCulture.DateTimeFormat);

    public string SizeString => Size?.ToSize();

    #endregion

    public FileClass(
        string fileName,
        string path,
        FileType type,
        bool isLink = false,
        UInt64? size = null,
        DateTime? modifiedTime = null,
        bool isTemp = false)
        : base(path, fileName, type)
    {
        Type = type;
        Size = size;
        ModifiedTime = modifiedTime;
        IsLink = isLink;

        GetIcon();
        TypeName = GetTypeName();
        IsTemp = isTemp;
        
        SortName = new(fileName);
    }

    public FileClass(FileClass other)
        : this(other.FullName, other.FullPath, other.Type, other.IsLink, other.Size, other.ModifiedTime, other.IsTemp)
    { }

    public FileClass(FilePath other)
        : this(other.FullName, other.FullPath, other.IsDirectory ? FileType.Folder : FileType.File)
    { }

    public FileClass(ShellItem windowsPath)
        : base(windowsPath)
    {
        Type = IsDirectory ? FileType.Folder : FileType.File;
        Size = IsDirectory ? null : (ulong?)windowsPath.FileInfo.Length;
        ModifiedTime = windowsPath.FileInfo?.LastWriteTime;
        IsLink = windowsPath.IsLink;

        GetIcon();
        TypeName = GetTypeName();

        SortName = new(FullName);
    }

    public FileClass(FileDescriptor fileDescriptor)
        : base(fileDescriptor.SourcePath, fileDescriptor.Name, fileDescriptor.IsDirectory ? FileType.Folder : FileType.File)
    {
        Size = (ulong?)fileDescriptor.Length;
        ModifiedTime = fileDescriptor.ChangeTimeUtc;
        Type = fileDescriptor.IsDirectory ? FileType.Folder : FileType.File;
    }

    public static FileClass GenerateAndroidFile(FileStat fileStat) => new(
        fileName: fileStat.FullName,
        path: fileStat.FullPath,
        type: fileStat.Type,
        size: fileStat.Size,
        modifiedTime: fileStat.ModifiedTime,
        isLink: fileStat.IsLink
    );

    public static FileClass FromWindowsPath(FilePath androidTargetPath, ShellItem windowsPath) =>
        new(androidTargetPath)
    {
        Size = windowsPath.IsFolder ? null : (ulong?)windowsPath.FileInfo.Length,
        ModifiedTime = windowsPath.FileInfo?.LastWriteTime,
    };

    public override void UpdatePath(string androidPath)
    {
        base.UpdatePath(androidPath);
        UpdateType();

        SortName = new(FullName);
    }

    public static string CutTypeString(DragDropEffects cutType) => cutType switch
    {
        DragDropEffects.Move => "Cut",
        DragDropEffects.Copy => "Copied",
        _ => "",
    };

    public void UpdateType()
    {
        TypeName = GetTypeName();
        GetIcon();
        OnPropertyChanged(nameof(ExtensionIsGlyph));
        OnPropertyChanged(nameof(ExtensionIsFontIcon));
    }

    public void UpdateSpecialType()
    {
        SpecialType = Type switch
        {
            FileType.Folder => SpecialFileType.Folder,
            FileType.Unknown => SpecialFileType.Unknown,
            FileType.BrokenLink => SpecialFileType.BrokenLink,
            _ => SpecialFileType.None
        };

        if (IsApk)
            SpecialType |= SpecialFileType.Apk;

        if (IsLink && Type is not FileType.BrokenLink)
            SpecialType |= SpecialFileType.LinkOverlay;
    }

    private string GetTypeName()
    {
        var type = Type switch
        {
            FileType.File => GetTypeName(FullName),
            FileType.Folder => "Folder",
            FileType.Unknown => "",
            _ => GetFileTypeName(Type),
        };

        if (IsLink && Type is not FileType.BrokenLink)
            type = string.IsNullOrEmpty(type) ? "Link" : $"{type} (Link)";

        return type;
    }

    public string GetTypeName(string fileName)
    {
        if (IsApk)
            return "Android Application Package";

        if (string.IsNullOrEmpty(fileName) || (IsHidden && FullName.Count(c => c == '.') == 1))
            return "File";

        if (Extension.Equals(".exe", StringComparison.CurrentCultureIgnoreCase))
            return "Windows Executable";

        if (!Ascii.IsValid(Extension))
        {
            if (ShortExtension.Length == 1)
                ExtensionIsGlyph = true;
            else if (ShortExtension.Length > 1)
                ExtensionIsFontIcon = true;
            else
            {
                ExtensionIsGlyph =
                ExtensionIsFontIcon = false;

                return $"{Extension[1..]} File";
            }

            return $"{ShortExtension} File";
        }
        else
        {
            ExtensionIsGlyph =
            ExtensionIsFontIcon = false;

            return NativeMethods.GetShellFileType(fileName);
        }
    }

    public FileSyncOperation PrepareDescriptors(VirtualFileDataObject vfdo, bool includeContent = true)
    {
        SyncFile target = new(FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, FullName, '\\'))
            { PathType = FilePathType.Windows };

        var fileOp = FileSyncOperation.PullFile(new(this), target, Data.CurrentADBDevice, App.Current.Dispatcher);
        fileOp.PropertyChanged += PullOperation_PropertyChanged;
        fileOp.VFDO = vfdo;

        string[] items = [FullName + (IsDirectory ? '/' : "")];
        if (includeContent && Children is not null)
        {
            items = [..items, ..Children];
        }

        // We only know the size of a single file beforehand
        long? size = IsDirectory ? null : (long?)Size;
        DateTime? date = IsDirectory ? null : ModifiedTime;

        Descriptors = items.Select(item => new FileDescriptor
        {
            Name = item.TrimEnd('/'),
            SourcePath = FullPath,
            IsDirectory = item[^1] is '/',
            Length = size,
            ChangeTimeUtc = date,
            StreamContents = stream =>
            {
                var isActive = App.Current.Dispatcher.Invoke(() => App.Current.MainWindow.IsActive);
                var operations = vfdo.Operations.Where(op => op.Status is FileOperation.OperationStatus.None);

                // When a VFDO that does not contain folders is sent to the clipboard, the shell immediately requests the file contents.
                // To prevent this, we refuse to give data when the app is focused.
                // When a legitimate request for data is made, the app can't be focused during the first file, but it can become focused again for the next files.
                if ((Data.CopyPaste.IsClipboard && isActive && operations.Any())
                    || !includeContent)
                    return;

#if !DEPLOY
                DebugLog.PrintLine($"Total uninitiated operations: {operations.Count()}");
#endif

                // Add all uninitiated operations to the queue.
                // For all consecutive files this list will be empty.
                foreach (var op in operations)
                {
                    App.Current.Dispatcher.Invoke(() => Data.FileOpQ.AddOperation(op));
                }

                // Wait for the operation to complete
                while (fileOp.Status is not FileOperation.OperationStatus.Completed)
                {
                    Thread.Sleep(100);
                }
                
                var file = FileHelper.ConcatPaths(Data.RuntimeSettings.TempDragPath, item, '\\');

                // Try 10 times to read from the file and write to the stream,
                // in case the file is still in use by ADB or hasn't appeared yet
                for (int i = 0; i < 10; i++)
                {
                    var abort = false;
                    ContentDialog dialog = null;

                    App.Current.Dispatcher.Invoke(() =>
                    {
                        if (!App.Current.MainWindow.IsActive)
                            return;

                        dialog = DialogService.ShowDialog(App.Current.FindResource("FileTransferGrid"), "Sending To Shell", buttonText: "Abort");
                        dialog.Closed += (s, e) =>
                        {
                            abort = true;
                        };
                    });

                    try
                    {
                        using (FileStream fs = new(file, FileMode.Open, FileAccess.Read))
                        {
                            // This uses a ~85K buffer, which is a pain for large files, but it gets cleared automatically
                            byte[] buffer = new byte[81920];
                            int totalRead = 0, read = 0;
                            Data.RuntimeSettings.CurrentTransferFile = item;

                            while (!abort && (read = fs.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                stream.Write(buffer, 0, read);
                                totalRead += read;

                                App.Current.Dispatcher.Invoke(() =>
                                {
                                    Data.RuntimeSettings.TransferProgress = 100 * (totalRead / (float)fs.Length);
                                });
                            }
                        }
                        if (dialog is not null)
                            App.Current.Dispatcher.Invoke(dialog.Hide);

                        break;
                    }
                    catch (Exception)
                    {
                        Thread.Sleep(100);
                        continue;
                    }
                    finally
                    {
                        if (dialog is not null)
                            App.Current.Dispatcher.Invoke(dialog.Hide);
                    }
                }
            }
        });

        return fileOp;
    }

    private static void PullOperation_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        var op = sender as FileSyncOperation;

        if (e.PropertyName != nameof(FileOperation.Status)
            || op.Status is FileOperation.OperationStatus.Waiting
            or FileOperation.OperationStatus.InProgress)
            return;

        if (op.Status is FileOperation.OperationStatus.Completed)
        {
            if (op.VFDO.CurrentEffect.HasFlag(DragDropEffects.Move))
            {
                // Delete file from device
                ShellFileOperation.SilentDelete(op.Device, op.FilePath.FullPath);

                // Remove file in UI if present
                if (op.Device.ID == Data.CurrentADBDevice.ID
                    && op.FilePath.ParentPath == Data.CurrentPath)
                {
                    op.Dispatcher.Invoke(() =>
                    {
                        Data.DirList.FileList.RemoveAll(f => f.FullPath == op.FilePath.FullPath);
                        FileActionLogic.UpdateFileActions();
                    });
                }

                if (op.VFDO.Operations.All(op => op.Status is FileOperation.OperationStatus.Completed))
                    Data.CopyPaste.Clear();
            }

            op.VFDO = null;
        }

        op.PropertyChanged -= PullOperation_PropertyChanged;
    }

    public void GetIcon()
    {
        var icons = FileToIconConverter.GetImage(this, true).ToArray();
        
        if (icons.Length > 0 && icons[0] is BitmapSource icon)
            Icon = icon;
        else
            Icon = null;

        if (icons.Length > 1 && icons[1] is BitmapSource icon2)
            IconOverlay = icon2;
        else
            IconOverlay = null;
    }

    public override string ToString()
    {
        if (TrashIndex is null)
        {
            return $"{DisplayName} \n{ModifiedTimeString} \n{TypeName} \n{SizeString}";
        }
        else
        {
            return $"{DisplayName} \n{TrashIndex.OriginalPath} \n{TrashIndex.ModifiedTimeString} \n{TypeName} \n{SizeString} \n{ModifiedTimeString}";
        }
    }

    protected override bool Set<T>(ref T storage, T value, [CallerMemberName] string propertyName = null)
    {
        if (propertyName is nameof(FullName) or nameof(Type) or nameof(IsLink))
        {
            UpdateType();
        }

        return base.Set(ref storage, value, propertyName);
    }

    public static explicit operator SyncFile(FileClass self)
        => new(self.FullPath, self.Type);
}

public class FileNameSort(string name) : IComparable
{
    public string Name { get; } = name;

    public override string ToString()
    {
        return Name;
    }

    public int CompareTo(object obj)
    {
        if (obj is not FileNameSort other)
            return 0;

        return NativeMethods.StringCompareLogical(Name, other.Name);
    }
}
