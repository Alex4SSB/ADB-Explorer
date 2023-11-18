using ADB_Explorer.Helpers;

namespace ADB_Explorer.Converters;

internal static class PluralityConverter
{
    public static string Convert<T>(ObservableProperty<IEnumerable<T>> value, string parameter = null) => Convert(value.Value, parameter);

    public static string Convert<T>(IEnumerable<T> value, string parameter = "")
    {
        int? count;

        if (value is IEnumerable<object> list)
            count = list.Count();
        else
            return null;

        return parameter + (count > 1 ? "s" : "");
    }
}
