namespace ADB_Explorer.Services;

internal static partial class FileActionLogic
{
    private static IReadOnlyList<string> PendingCompressSourcePaths { get; set; } = [];

    private static FileClass? PendingCompressTemp { get; set; }

    public static bool IsPendingCompress { get; private set; }

    public static IReadOnlyList<string> GetPendingCompressSourcePaths() => PendingCompressSourcePaths;

    public static void BeginCompressTo(string extension)
    {
        PendingCompressSourcePaths = [.. Data.SelectedFiles.Select(f => f.FullPath)];
        PendingCompressTemp = null;
        IsPendingCompress = true;
        Data.RequestCompressTo(extension);
    }

    public static void SetPendingCompressTemp(FileClass file) => PendingCompressTemp = file;

    public static void CancelPendingCompress(FileClass? file = null)
    {
        if (!IsPendingCompress)
            return;

        if (file is not null
            && PendingCompressTemp is not null
            && !ReferenceEquals(file, PendingCompressTemp))
            return;

        IsPendingCompress = false;
        PendingCompressTemp = null;
        PendingCompressSourcePaths = [];
    }

    private static bool TryConsumePendingCompress(FileClass file, out IReadOnlyList<string> sourcePaths)
    {
        sourcePaths = [];
        if (!IsPendingCompress || !ReferenceEquals(file, PendingCompressTemp))
            return false;

        sourcePaths = PendingCompressSourcePaths;
        IsPendingCompress = false;
        PendingCompressTemp = null;
        PendingCompressSourcePaths = [];
        return true;
    }
}
