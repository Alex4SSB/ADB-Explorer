using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

public partial class TrashIndexer : ObservableObject
{
    [ObservableProperty]
    private string _recycleName;

    [ObservableProperty]
    private string originalPath;

    [ObservableProperty]
    private DateTime? _dateModified;

    public string ModifiedTimeString => TabularDateFormatter.Format(DateModified, Thread.CurrentThread.CurrentCulture);

    public string IndexerPath => $"{AdbExplorerConst.RECYCLE_PATH}/.{RecycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";

    public string ParentPath
    {
        get
        {
            int originalIndex = OriginalPath.LastIndexOf('/');
            Index index;
            if (originalIndex == 0)
                index = 1;
            else if (originalIndex < 0)
                index = ^0;
            else
                index = originalIndex;

            return OriginalPath[..index];
        }
    }

    public TrashIndexer()
    { }

    public TrashIndexer(string recycleIndex) : this(recycleIndex.Split('|'))
    { }

    public TrashIndexer(params string[] recycleIndex) : this(recycleIndex[0], recycleIndex[1], recycleIndex[2])
    { }

    public TrashIndexer(string recycleName, string originalPath, string dateModified)
        : this(recycleName, originalPath, DateTime.TryParseExact(dateModified, AdbExplorerConst.ADB_EXPLORER_DATE_FORMAT, null, DateTimeStyles.None, out var res) ? res : null)
    { }

    public TrashIndexer(string recycleName, string originalPath, DateTime? dateModified)
    {
        RecycleName = recycleName;
        OriginalPath = originalPath;
        DateModified = dateModified;
    }

    public TrashIndexer(FileMoveOperation op)
    {
        RecycleName = op.RecycleName;
        OriginalPath = op.FilePath.FullPath;
        DateModified = op.DateModified;
    }

    public override string ToString()
    {
        var date = DateModified is null ? "?" : DateModified.Value.ToString(AdbExplorerConst.ADB_EXPLORER_DATE_FORMAT);
        return $"{RecycleName}|{OriginalPath}|{date}";
    }
}
