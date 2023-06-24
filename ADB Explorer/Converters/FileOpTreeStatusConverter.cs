using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Converters;

internal class FileOpTreeStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is ObservableList<SyncFile> children)
        {
            var total = children.Count;
            var failed = children.Count(c => c.ProgressUpdates.OfType<SyncErrorInfo>().Any());
            var completed = total - failed;
            
            return $"({(failed > 0 ? $"{failed} Failed, " : "")}{completed} Completed)";
        }
        else if (value is ObservableList<FileOpProgressInfo> updates)
        {
            var fail = updates.OfType<SyncErrorInfo>();
            return fail.Any() ? "Error: " + fail.Last().Message : "Completed";
        }
        
        throw new NotSupportedException();
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
