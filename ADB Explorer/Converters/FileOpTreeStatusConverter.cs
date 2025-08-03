using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using static ADB_Explorer.Converters.FileOpStatusConverter;

namespace ADB_Explorer.Converters;

internal class FileOpTreeStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string result = "";
        if (value is ObservableList<SyncFile> children)
        {
            var (failed, type) = CountFails(children);

            result = StatusString(type, children.Count - failed, failed);
        }
        else if (value is ObservableList<FileOpProgressInfo> updates)
        {
            result = StatusString(updates.First().GetType(), message: updates.OfType<FileOpErrorInfo>().LastOrDefault()?.Message);
        }

        if (parameter is "Completed")
        {
            return result == Strings.Resources.S_FILEOP_COMPLETED;
        }
        else if (parameter is "Validated")
        {
            return result == Strings.Resources.S_FILEOP_VALIDATED;
        }
        else
            return result;

        throw new NotSupportedException();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

public static class FileOpStatusConverter
{
    public static string StatusString(Type type, int completed = 0, int failed = -1, string message = "", bool total = false)
    {
        if (!string.IsNullOrEmpty(message))
        {
            return message.StartsWith(Strings.Resources.S_REDIRECTION)
                ? message
                : string.Format(Strings.Resources.S_FILEOP_ERROR, message);
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