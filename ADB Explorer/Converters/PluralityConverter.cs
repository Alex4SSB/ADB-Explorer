using ADB_Explorer.Helpers;

namespace ADB_Explorer.Converters;

internal class PluralityConverter : IValueConverter
{
    public static string Convert<T>(IEnumerable<T> value, string parameter = null) => (string)new PluralityConverter().Convert(value, typeof(T), parameter);

    public static string Convert<T>(ObservableProperty<IEnumerable<T>> value, string parameter = null) => (string)new PluralityConverter().Convert(value.Value, typeof(T), parameter);


    public object Convert(object value, Type targetType = null, object parameter = null, CultureInfo culture = null)
    {
        int? count;

        if (value is IEnumerable<object> list)
            count = list.Count();
        else
            return null;

        return (parameter is string ? parameter : "") + (count > 1 ? "s" : "");
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
