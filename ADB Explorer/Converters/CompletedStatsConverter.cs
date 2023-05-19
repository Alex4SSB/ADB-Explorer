using ADB_Explorer.ViewModels;

namespace ADB_Explorer.Converters;

internal class CompletedStatsConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var result = "";
        if (value is not CompletedSyncProgressViewModel info)
            return result;

        if (string.IsNullOrEmpty(info.TotalSize))
            return result;

        result += info.TotalSize;
        if (string.IsNullOrEmpty(info.TotalTime))
            return result;

        result += $" in {info.TotalTime}";

        if (string.IsNullOrEmpty(info.AverageRateString))
            return result;

        result += $" ({info.AverageRateString})";
        return result;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
