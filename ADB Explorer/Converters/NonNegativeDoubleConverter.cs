namespace ADB_Explorer.Converters;

public class NonNegativeDoubleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is double d ? Math.Max(0d, d) : value;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value;
}
