using ADB_Explorer.Helpers;
using ADB_Explorer.Services;
using Vanara.Windows.Shell;

namespace ADB_Explorer.Models;

public class SyncFile : FilePath
{
    public ObservableList<FileOpProgressInfo> ProgressUpdates { get; } = [];

    public ObservableList<SyncFile> Children { get; } = [];

    public ulong? Size { get; }

    public SyncFile(string androidPath, FileType fileType = FileType.File)
        : base(androidPath, fileType: fileType)
    {

    }

    public SyncFile(ShellItem windowsPath)
        : base(windowsPath)
    {
        Size = IsDirectory ? null : (ulong?)windowsPath.FileInfo.Length;
    }

    public SyncFile(FileClass fileClass)
        : base(fileClass.FullPath, fileClass.FullName, fileClass.Type)
    {
        Size = fileClass.Size;
    }

    public void AddUpdates(params FileOpProgressInfo[] newUpdates)
        => AddUpdates(newUpdates.Where(o => o is not null));

    public void AddUpdates(IEnumerable<FileOpProgressInfo> newUpdates, FileOperation fileOp = null)
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
                bool isDir = !group.Key.Equals(group.First().AndroidPath);
                file = new(group.Key, isDir ? FileType.Folder : FileType.File);

                ExecuteInDispatcher(() =>
                {
                    Children.Add(file);
                    
                    OnPropertyChanged(nameof(Children));
                });
            }

            file.AddUpdates(group);
        }
    }

    public string DirectChildPath(string fullPath)
        => FileHelper.DirectChildPath(FullPath, fullPath);
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
