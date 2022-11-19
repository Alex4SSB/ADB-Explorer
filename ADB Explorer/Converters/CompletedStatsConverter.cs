using static ADB_Explorer.Services.FileSyncOperation;

namespace ADB_Explorer.Converters;

internal class CompletedStatsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is CompletedInfo info
            ? $"{info.TotalSize} in {info.TotalTime} ({info.AverageRateString})"
            : "";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
