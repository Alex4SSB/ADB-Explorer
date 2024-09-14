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

    /// <summary>
    /// Determines whether all elements of a sequence satisfy a condition
    /// </summary>
    /// <returns><see langword="true"/> if the source sequence contains any elements and every element passes the test in the specified predicate; otherwise, <see langword="false"/></returns>
    public static bool AnyAll<T>(this IEnumerable<T> source, Func<T, bool> predicate)
    {
        return source.Any() && source.All(predicate);
    }

    /// <summary>
    /// Performs the specified <see cref="Action"/> on each element of the collection
    /// </summary>
    public static void ForEach<T>(this IEnumerable<T> self, Action<T> action)
    {
        foreach (var item in self)
        {
            action(item);
        }
    }
}
