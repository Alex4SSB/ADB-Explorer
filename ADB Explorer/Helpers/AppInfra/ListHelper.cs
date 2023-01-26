namespace ADB_Explorer.Helpers;

internal static class ListHelper
{
    public static ListSortDirection Invert(ListSortDirection? value)
    {
        return value is ListSortDirection and ListSortDirection.Ascending ? ListSortDirection.Descending : ListSortDirection.Ascending;
    }
}
