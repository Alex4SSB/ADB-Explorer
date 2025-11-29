using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Converters;

internal class CompletedStatsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CompletedSyncProgressViewModel info || string.IsNullOrEmpty(info.TotalSize))
            return "";

        string format = Strings.Resources.S_FILE_PROGRESS_TRANSFER;

        if (string.IsNullOrEmpty(info.TotalTime))
            return info.TotalSize;

        if (string.IsNullOrEmpty(info.AverageRateString))
            return string.Format(format[..(format.IndexOf("{1}") + 3)].Trim(), info.TotalSize, info.TotalTime);

        return string.Format(format, info.TotalSize, info.TotalTime, info.AverageRateString);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
