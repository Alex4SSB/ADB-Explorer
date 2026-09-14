using ADB_Explorer.Helpers;

namespace ADB_Explorer.Converters;

/// <summary>
/// Returns where the current search query matched within a file's display name, as (Start,
/// Length) ranges for <see cref="Emoji.Wpf.TextBlock.HighlightRanges"/>. For a plain query,
/// that's every literal occurrence; for a wildcard query ('*', '?', '[...]'), it's every
/// literal segment of the pattern at its matched position (the wildcard parts themselves match
/// "anything", so they aren't highlighted). Files that only matched by content (not by name)
/// naturally get no ranges, since the query isn't in the name either way.
/// </summary>
public class SearchHighlightRangesConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not string name
            || values[1] is not string query
            || values[2] is not bool caseSensitive
            || string.IsNullOrEmpty(name)
            || string.IsNullOrEmpty(query))
        {
            return Array.Empty<(int Start, int Length)>();
        }

        if (FileHelper.ContainsWildcard(query))
            return FileHelper.GetWildcardHighlightRanges(name, query, caseSensitive);

        var comparison = caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var ranges = new List<(int Start, int Length)>();

        for (int i = 0; (i = name.IndexOf(query, i, comparison)) >= 0; i += query.Length)
            ranges.Add((i, query.Length));

        return ranges;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
