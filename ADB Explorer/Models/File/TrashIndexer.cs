using ADB_Explorer.Helpers;
using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

public partial class TrashIndexer : ObservableObject
{
    [ObservableProperty]
    public partial string RecycleName { get; set; }

    [ObservableProperty]
    public partial string OriginalPath { get; set; }

    [ObservableProperty]
    public partial DateTime? DateModified { get; set; }

    public string ModifiedTimeString => TabularDateFormatter.Format(DateModified, Data.Settings.ActualFormatCulture);

    public string IndexerPath => $"{AdbExplorerConst.RECYCLE_PATH}/.{RecycleName}{AdbExplorerConst.RECYCLE_INDEX_SUFFIX}";

    public string ParentPath
    {
        get
        {
            if (string.IsNullOrEmpty(OriginalPath))
                return "";

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

    public TrashIndexer(string recycleIndex)
    {
        if (!TryParse(recycleIndex, out var parsed))
            throw new FormatException($"Invalid recycle index line: {recycleIndex}");

        RecycleName = parsed.RecycleName;
        OriginalPath = parsed.OriginalPath;
        DateModified = parsed.DateModified;
    }

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

    public static IReadOnlyList<TrashIndexer> ParseLines(string text)
    {
        if (string.IsNullOrEmpty(text))
            return [];

        List<TrashIndexer> result = [];
        foreach (var line in text.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParse(line, out var indexer))
                result.Add(indexer);
        }

        ResolveRecycledOriginals(result);
        return result;
    }

    public static bool TryParse(string line, out TrashIndexer indexer)
    {
        indexer = null;
        if (string.IsNullOrWhiteSpace(line))
            return false;

        var parts = line.Split('|');
        if (parts.Length < 3)
            return false;

        var recycleName = NormalizeIndexField(parts[0]);
        var date = NormalizeIndexField(parts[^1]);
        var originalPath = NormalizeIndexField(string.Join('|', parts[1..^1]));
        if (string.IsNullOrEmpty(recycleName) || string.IsNullOrEmpty(originalPath))
            return false;

        indexer = new TrashIndexer(recycleName, originalPath, date);
        return true;
    }

    /// <summary>
    /// Follows OriginalPath values that still point at a recycle-bin item (from a later
    /// re-recycle) until the real source path is found.
    /// </summary>
    public static void ResolveRecycledOriginals(IList<TrashIndexer> indexers)
    {
        Dictionary<string, TrashIndexer> byName = new(StringComparer.Ordinal);
        foreach (var indexer in indexers)
        {
            if (!string.IsNullOrEmpty(indexer.RecycleName))
                byName[indexer.RecycleName] = indexer;
        }

        foreach (var indexer in indexers)
            indexer.OriginalPath = ResolveOriginalPath(indexer, byName, []);
    }

    private static string ResolveOriginalPath(TrashIndexer indexer, Dictionary<string, TrashIndexer> byName, HashSet<string> visiting)
    {
        var path = indexer.OriginalPath;
        if (string.IsNullOrEmpty(path) || !IsTrashPath(path))
            return path;

        var previousName = FileHelper.GetFullName(path);
        if (string.IsNullOrEmpty(previousName) || previousName == indexer.RecycleName)
            return path;

        if (!visiting.Add(indexer.RecycleName ?? previousName))
            return path;

        if (!byName.TryGetValue(previousName, out var previous))
            return path;

        return ResolveOriginalPath(previous, byName, visiting);
    }

    /// <summary>
    /// Index lines are written via quoted <c>echo</c>. Pipes inside quotes were historically
    /// backslash-escaped, so RecycleName/OriginalPath can pick up a trailing <c>\</c>.
    /// </summary>
    private static string NormalizeIndexField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        var trimmed = value.Trim().Trim('"').Trim();
        return trimmed.Trim('\\');
    }

    public bool MatchesRecycleFile(string fileName)
    {
        if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(RecycleName))
            return false;

        if (RecycleName == fileName)
            return true;

        return FileHelper.GetFullName(RecycleName) == fileName;
    }

    private static bool IsTrashPath(string path)
        => AdbExplorerConst.POSSIBLE_RECYCLE_PATHS.Any(recycle =>
            path == recycle || path.StartsWith($"{recycle}/", StringComparison.Ordinal));

    public override string ToString()
    {
        var date = DateModified is null ? "?" : DateModified.Value.ToString(AdbExplorerConst.ADB_EXPLORER_DATE_FORMAT);
        return $"{RecycleName}|{OriginalPath}|{date}";
    }
}
