namespace ADB_Explorer.Services;

public static class FileOpStatusFormatter
{
    public static string StatusString(Type type, int completed = 0, int failed = -1, string message = "", bool total = false)
    {
        if (!string.IsNullOrEmpty(message))
        {
            return string.Format(Strings.Resources.S_FILEOP_ERROR, message);
        }

        var completedString = (type == typeof(HashFailInfo) || type == typeof(HashSuccessInfo))
            ? Strings.Resources.S_FILEOP_VALIDATED
            : Strings.Resources.S_FILEOP_COMPLETED;

        if (failed == -1)
            return completedString;

        if (type == typeof(ShellErrorInfo))
            return $"({string.Format(Strings.Resources.S_FILEOP_SUBITEM_FAILED, failed)})";

        var failedString = failed > 0 ? $"{string.Format(Strings.Resources.S_FILEOP_SUBITEM_FAILED, failed)}, " : "";

        return $"({(total ? $"{Strings.Resources.S_FILEOP_TOTAL} " : "")}{failedString}{completed} {completedString})";
    }

    public static (int, Type) CountFails(ObservableList<SyncFile> children)
    {
        int total = 0;

        foreach (var item in children.Where(c => c.Children.Count > 0))
        {
            if (CountFails(item.Children).Item1 > 0)
                total++;
        }

        total += children.Count(c => c.Children.Count == 0 && c.ProgressUpdates.OfType<FileOpErrorInfo>().Any());

        var type = typeof(SyncErrorInfo);
        if (children.Any(c => c.ProgressUpdates.Any(u => u is ShellErrorInfo)))
            type = typeof(ShellErrorInfo);
        else if (children.Any(c => c.ProgressUpdates.Any(u => u is HashFailInfo or HashSuccessInfo)))
            type = typeof(HashFailInfo);

        return (total, type);
    }
}