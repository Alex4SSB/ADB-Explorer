namespace ADB_Explorer.Converters;

public class StringFormatConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is not string format)
            return string.Empty;

        try
        {
            return string.Format(format, values);
        }
        catch
        {
            return format;
        }
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
