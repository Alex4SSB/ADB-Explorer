namespace ADB_Explorer.Converters;

public class CompletedStatsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not CompletedSyncProgressViewModel info || string.IsNullOrEmpty(info.TotalSize))
            return "";

        if (string.IsNullOrEmpty(info.TotalTime))
            return info.TotalSize;

        if (string.IsNullOrEmpty(info.AverageRateString))
            return string.Format(Strings.Resources.S_FILE_TRANSFER_SIZE_TIME, info.TotalSize, info.TotalTime);

        return string.Format(Strings.Resources.S_FILE_PROGRESS_TRANSFER, info.TotalSize, info.TotalTime, info.AverageRateString);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
