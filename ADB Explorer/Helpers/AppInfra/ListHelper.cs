namespace ADB_Explorer.Helpers;

internal static class ListHelper
{
    public static ListSortDirection Invert(ListSortDirection? value)
    {
        return value is ListSortDirection and ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
    }

    /// <summary>
    /// Appends an IEnumerable to the end of another IEnumerable.<br />
    /// *** ENUMERATES BOTH OF THE ENUMERABLES ***
    /// </summary>
    public static IEnumerable<T> AppendRange<T>(this IEnumerable<T> self, IEnumerable<T> other)
    {
        foreach (var item in self)
        {
            yield return item;
        }

        foreach (var item in other)
        {
            yield return item;
        }
    }
}
