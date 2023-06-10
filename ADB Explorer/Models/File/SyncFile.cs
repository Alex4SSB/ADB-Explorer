using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

public class SyncFile : FilePath
{
    public ObservableList<FileOpProgressInfo> ProgressUpdates { get; }

    public SyncFile(string androidPath, FileType fileType = FileType.File)
        : base(androidPath, fileType: fileType)
    {

    }

    public SyncFile(ObservableList<FileOpProgressInfo> updates)
        : this(updates.First().AndroidPath)
    {
        ProgressUpdates = updates;
    }
}
