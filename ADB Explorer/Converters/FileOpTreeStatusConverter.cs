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
            int shellUpdates = 0;
            var failed = CountFails(children, ref shellUpdates);
            var completed = total - failed;

            if (shellUpdates > 0)
                return $"({failed} Failed)";
            else
                return $"({(failed > 0 ? $"{failed} Failed, " : "")}{completed} Completed)";
        }
        else if (value is ObservableList<FileOpProgressInfo> updates)
        {
            var fail = updates.OfType<FileOpErrorInfo>();
            return fail.Any() ? "Error: " + fail.Last().Message : "Completed";
        }
        
        throw new NotSupportedException();

        static int CountFails(ObservableList<SyncFile> children, ref int shellUpdates)
        {
            int total = 0;

            foreach (var item in children.Where(c => c.Children.Count > 0))
            {
                if (CountFails(item.Children, ref shellUpdates) > 0)
                    total++;
            }

            total += children.Count(c => c.Children.Count == 0 && c.ProgressUpdates.OfType<FileOpErrorInfo>().Any());

            shellUpdates += children.Select(c => c.ProgressUpdates.OfType<ShellErrorInfo>().Count()).Sum();

            return total;
        }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
