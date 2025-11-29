namespace ADB_Explorer.Converters;

public class FileOpProgressConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value switch
        {
            double val when val
                is double.NaN
                or double.PositiveInfinity
                or double.NegativeInfinity => 0.0,
            int or long or double => value,
            _ => 0.0
        };
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;
    }
}
