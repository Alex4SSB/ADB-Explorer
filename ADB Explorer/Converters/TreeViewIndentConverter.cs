namespace ADB_Explorer.Converters;

internal class TreeViewIndentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Thickness margin)
            return new Thickness(-1 * margin.Left, margin.Top, margin.Right, margin.Bottom);

        return null;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
