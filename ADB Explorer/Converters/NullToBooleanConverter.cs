namespace ADB_Explorer.Converters;

/// <summary>
/// True when the bound value is non-null. Used to gate a Style Setter so it only fires when an
/// inherited attached property (e.g. StyleHelper.MenuItemForeground) actually has a value —
/// leaving the target property alone otherwise, so it falls through to whatever it would have
/// inherited normally instead of being forced to a hardcoded fallback brush.
/// </summary>
public class NullToBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
