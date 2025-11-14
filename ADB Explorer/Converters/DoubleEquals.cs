namespace ADB_Explorer.Converters;

public class DoubleEquals : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (parameter is string paramString)
        {
            if (double.TryParse(paramString, out double result))
            {
                var val = double.Parse($"{value}", CultureInfo.InvariantCulture);

                return val == result;
            }
        }

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return null;
    }
}
