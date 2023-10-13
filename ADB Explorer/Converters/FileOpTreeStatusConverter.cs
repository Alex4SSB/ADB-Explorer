using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;
using static ADB_Explorer.Converters.FileOpStatusConverter;

namespace ADB_Explorer.Converters;

internal class FileOpTreeStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ObservableList<SyncFile> children)
        {
            var (failed, type) = CountFails(children);

            return StatusString(type, children.Count - failed, failed);
        }
        else if (value is ObservableList<FileOpProgressInfo> updates)
        {
            return StatusString(updates.First().GetType(), message: updates.OfType<FileOpErrorInfo>().LastOrDefault()?.Message);
        }
        
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
            return $"Error: {message}";

        var completedString = (type == typeof(HashFailInfo) || type == typeof(HashSuccessInfo))
            ? "Validated" : "Completed";

        if (failed == -1)
            return completedString;

        if (type == typeof(ShellErrorInfo))
            return $"({failed} Failed)";

        var failedString = failed > 0 ? $"{failed} Failed, " : "";
        
        return $"({(total ? "Total: " : "")}{failedString}{completed} {completedString})";
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