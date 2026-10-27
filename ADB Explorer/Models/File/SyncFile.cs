using Vanara.Windows.Shell;

namespace ADB_Explorer.Models;

public class SyncFile : FilePath
{
    public ObservableList<FileOpProgressInfo> ProgressUpdates { get; private set; } = [];

    public FileOpProgressInfo LastUpdate => ProgressUpdates.LastOrDefault();

    public double? CurrentPercentage => LastUpdate is AdbSyncProgressInfo adbInfo ? adbInfo.CurrentFilePercentage : null;

    public long? BytesTransferred => LastUpdate is AdbSyncProgressInfo adbInfo ? adbInfo.CurrentFileBytesTransferred : null;

    public ObservableList<SyncFile> Children { get; private set; } = [];

    public long? Size { get; set; }

    public double? UnixTime { get; set; }

    private readonly DateTime? _dateModified;
    public DateTime? DateModified
    {
        get
        {
            if (!Data.Settings.KeepDateModified)
                return null;

            return _dateModified ?? UnixTime.FromUnixTime();
        }
    }

    /// <summary>Has no path on the file system (a zip member, a file on a phone over MTP, a FileContents stream).</summary>
    public bool IsVirtual { get; set; }

    /// <summary>Opens a virtual file's content; not set for files on the file system.</summary>
    public Func<Stream>? OpenContent { get; set; }

    /// <summary>When the source file was last modified, regardless of whether it's kept on the target.</summary>
    public DateTime? SourceModified => _dateModified ?? UnixTime.FromUnixTime();

    public Stream OpenRead()
        => OpenContent?.Invoke() ?? new FileStream(FullPath, FileMode.Open, FileAccess.Read, FileShare.Read);

    // Virtual trees with no shell folder to list them from (FileContents), by root path, while they're being pushed
    private static readonly ConcurrentDictionary<string, SyncFile> _virtualRoots = new(StringComparer.OrdinalIgnoreCase);

    public static void RegisterVirtualRoot(SyncFile root) => _virtualRoots[root.FullPath] = root;

    public static void UnregisterVirtualRoot(SyncFile root) => _virtualRoots.TryRemove(root.FullPath, out _);

    public static SyncFile? FindVirtualRoot(string path) => _virtualRoots.GetValueOrDefault(path);

    public SyncFile(string androidPath, FileType fileType = FileType.File)
        : base(androidPath, fileType: fileType)
    {

    }

    public SyncFile(ShellItem windowsPath, bool includeContent = false)
        : base(windowsPath)
    {
        (Size, _dateModified) = FileHelper.GetShellSizeDate(windowsPath, IsDirectory);

        if (!windowsPath.IsFileSystem)
        {
            IsVirtual = true;
            if (!IsDirectory)
                OpenContent = () => ShellContentStream.Open(ShellItem, FullPath);
        }

        if (includeContent && IsDirectory)
        {
            Children = [.. GetFolderTree((ShellFolder)windowsPath)];
        }
    }

    /// <summary>A file or folder described by the shell (e.g. in a FileGroupDescriptor) with no path on disk.</summary>
    public SyncFile(string windowsPath, bool isDirectory, long? size, DateTime? modified, Func<Stream>? openContent)
        : base(windowsPath, fileType: isDirectory ? FileType.Folder : FileType.File)
    {
        PathType = FilePathType.Windows;
        IsVirtual = true;
        Size = isDirectory ? null : size;
        _dateModified = modified;
        OpenContent = openContent;
        ProgressUpdates = [new AdbSyncProgressInfo(windowsPath, null, null, null)];
    }

    public SyncFile(FileClass fileClass, IEnumerable<FolderTree>? tree = null)
        : base(fileClass.FullPath, fileClass.FullName, fileClass.Type)
    {
        ShellLsSize = fileClass.ShellLsSize is > 0 ? fileClass.ShellLsSize : null;
        Size = ShellLsSize ?? fileClass.Size;
        UnixTime = fileClass.ModifiedTime.ToUnixTime();

        if (tree is not null && IsDirectory)
            Children = [.. GetFolderTree(tree, FullPath)];
    }

    public SyncFile(SyncFile other) : this(new FileClass(other))
    { }

    static IEnumerable<SyncFile> GetFolderTree(ShellFolder rootFolder)
    {
        foreach (var child in rootFolder)
        {
            SyncFile syncFile;
            try
            {
                if (child.IsNonArchiveFolder())
                {
                    syncFile = new(child)
                    {
                        Children = [.. GetFolderTree((ShellFolder)child)],
                        ProgressUpdates = [new AdbSyncProgressInfo(child.ParsingName, null, null, null)]
                    };
                }
                else
                {
                    syncFile = new(child)
                    {
                        ProgressUpdates = [new AdbSyncProgressInfo(child.ParsingName, null, null, null)]
                    };
                }
            }
            catch
            {
                continue;
            }

            yield return syncFile;
        }
    }

    static IEnumerable<SyncFile> GetFolderTree(IEnumerable<FolderTree> tree, string parent)
    {
        // empty folder
        if (!tree.Any())
            yield break;

        var groups = tree.GroupBy(f => FileHelper.DirectChildPath(parent, f.Name));

        foreach (var group in groups.Where(g => g.Key is not null))
        {
            var fullPath = group.Key;

            // Nested content under this path (or an explicit dir marker) means a folder — not an empty file.
            var isFolder = group.Any(g => g.IsFolder)
                || group.Any(g => g.Name.Length > fullPath.Length);

            if (isFolder)
            {
                var children = GetFolderTree(
                    group.Where(g => !g.Name.Equals(fullPath, StringComparison.Ordinal)),
                    fullPath);

                yield return new(fullPath, FileType.Folder)
                {
                    Children = [.. children],
                    ProgressUpdates = [new AdbSyncProgressInfo(fullPath, null, null, null)]
                };
            }
            else
            {
                yield return new(fullPath, FileType.File)
                {
                    Size = group.First().Size,
                    UnixTime = group.First().Date,
                    ProgressUpdates = [new AdbSyncProgressInfo(fullPath, null, null, null)]
                };
            }
        }
    }

    public void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => AddUpdates(newUpdates.Where(o => o is not null));

    public void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates, FileOperation? fileOp = null)
    {
        if (!newUpdates.Any())
            return;

        if (!IsDirectory || newUpdates.All(u => u.AndroidPath is not null && u.AndroidPath.Equals(FullPath)))
        {
            if (ProgressUpdates.Count > 0 && ProgressUpdates.Last().GetType() == newUpdates.First().GetType())
                ProgressUpdates = [.. newUpdates];
            else
                ProgressUpdates.AddRange(newUpdates);

            return;
        }

        if (fileOp is FileSyncOperation)
        {
            foreach (var update in newUpdates)
            {
                if (string.IsNullOrEmpty(update.AndroidPath))
                    update.SetPathToCurrent(fileOp);
            }
        }
        else
            newUpdates = newUpdates.Where(u => !string.IsNullOrEmpty(u.AndroidPath));

        var groups = newUpdates.GroupBy(update => DirectChildPath(update.AndroidPath));

        foreach (var group in groups.Where(g => g.Key is not null))
        {
            SyncFile file = Children.FirstOrDefault(child => child.FullPath.Equals(group.Key));

            if (file is null)
            {
                bool isDir = !group.Key.Equals(group.First().AndroidPath) || group.Key[^1] is '/' or '\\';
                file = new(group.Key, isDir ? FileType.Folder : FileType.File)
                {
                    PathType = PathType
                };

                Children.Add(file);
                OnPropertyChanged(nameof(Children));
            }

            file.AddUpdates(group);
        }
    }

    public string DirectChildPath(string fullPath)
        => FileHelper.DirectChildPath(FullPath, fullPath);

    public IEnumerable<SyncFile> AllChildren()
    {
        foreach (var child in Children)
        {
            yield return child;

            foreach (var grandChild in child.AllChildren())
            {
                yield return grandChild;
            }
        }
    }

    public static SyncFile MergeToWindowsPath(SyncFile syncFile, string windowsPath)
    {
        SyncFile copy = new(syncFile);

        copy.UpdatePath(FileHelper.ConcatPaths(windowsPath, syncFile.FullName, '\\'));
        copy.PathType = FilePathType.Windows;

        return copy;
    }

    /// <summary>
    /// Recursively clears progress updates for this file and all children,
    /// and disposes any ShellItem COM wrappers to free unmanaged memory.
    /// </summary>
    public void ClearAll()
    {
        ProgressUpdates.Clear();
        ShellItem?.Dispose();
        ShellItem = null;

        foreach (var child in Children)
        {
            child.ClearAll();
        }

        Children.Clear();
    }
}
