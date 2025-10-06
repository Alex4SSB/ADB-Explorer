using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Models;

public class SyncFile : FilePath
{
    public ObservableList<FileOpProgressInfo> ProgressUpdates { get; private set; } = [];

    public ObservableList<SyncFile> Children { get; private set; } = [];

    public ulong? Size { get; set; }

    public SyncFile(string androidPath, FileType fileType = FileType.File)
        : base(androidPath, fileType: fileType)
    {

    }

    public SyncFile(ShellItem windowsPath, bool includeContent = false)
        : base(windowsPath)
    {
        Size = IsDirectory ? null : (ulong?)windowsPath.FileInfo.Length;

        if (includeContent && IsDirectory)
        {
            Children = [.. GetFolderTree((ShellFolder)windowsPath)];
        }
    }

    public SyncFile(FileClass fileClass)
        : base(fileClass.FullPath, fileClass.FullName, fileClass.Type)
    {
        Size = fileClass.Size;
    }

    public SyncFile(SyncFile other) : this(new FileClass(other))
    { }

    static IEnumerable<SyncFile> GetFolderTree(ShellFolder rootFolder)
    {
        foreach (var child in rootFolder)
        {
            if (child.IsNonArchiveFolder())
            {
                yield return new(child)
                {
                    Children = [.. GetFolderTree((ShellFolder)child)],
                    ProgressUpdates = [new AdbSyncProgressInfo(child.ParsingName, null, null, null)]
                };
            }
            else
                yield return new(child)
                {
                    ProgressUpdates = [new AdbSyncProgressInfo(child.ParsingName, null, null, null)]
                };
        }
    }

    public void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => AddUpdates(newUpdates.Where(o => o is not null));

    public void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates, FileOperation fileOp = null, bool executeInDispatcher = true)
    {
        if (!newUpdates.Any())
            return;

        if (!IsDirectory || newUpdates.All(u => u.AndroidPath is not null && u.AndroidPath.Equals(FullPath)))
        {
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

                ExecuteInDispatcher(() =>
                {
                    Children.Add(file);
                    
                    OnPropertyChanged(nameof(Children));
                }, executeInDispatcher);
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

    public static SyncFile MergeToWindowsPath(SyncFile syncFile, ShellItem windowsPath)
    {
        SyncFile copy = new(syncFile);

        copy.UpdatePath(FileHelper.ConcatPaths(windowsPath.ParsingName, syncFile.FullName, '\\'));
        copy.PathType = FilePathType.Windows;

        return copy;
    }
}

public class SyncFileComparer : IEqualityComparer<SyncFile>
{
    public bool Equals(SyncFile x, SyncFile y)
        => x.FullPath.Equals(y.FullPath);

    public int GetHashCode([DisallowNull] SyncFile obj)
    {
        throw new NotImplementedException();
    }
}
