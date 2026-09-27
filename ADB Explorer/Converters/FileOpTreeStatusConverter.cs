using static ADB_Explorer.Services.FileOpStatusFormatter;

namespace ADB_Explorer.Converters;

public class FileOpTreeStatusConverter : IValueConverter
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
