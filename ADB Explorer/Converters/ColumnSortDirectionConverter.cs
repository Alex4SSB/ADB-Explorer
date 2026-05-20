using ADB_Explorer.Controls;

namespace ADB_Explorer.Converters;

/// <summary>
/// Returns the sort direction when the current <see cref="SortingSelector.SortingProperty"/>
/// matches the column specified by <see cref="ConverterParameter"/>; otherwise returns <see langword="null"/>.
/// </summary>
public class ColumnSortDirectionConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not ListSortDirection dir
            || values[1] is not SortingSelector.SortingProperty col
            || parameter is not string s
            || !Enum.TryParse<SortingSelector.SortingProperty>(s, out var expected))
            return null;

        return col == expected ? dir : null;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
