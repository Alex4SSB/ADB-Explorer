namespace ADB_Explorer.Helpers;

public static partial class TabularDateFormatter
{
    /// <summary>
    /// Formats a DateTime using the given culture's date/time format,
    /// but ensures zero-padding for consistent column width.
    /// </summary>
    public static string Format(DateTime? dateTime, CultureInfo culture)
    {
        if (dateTime is null)
            return string.Empty;

        // Build combined pattern from ShortDate and LongTime patterns
        string pattern = culture.DateTimeFormat.ShortDatePattern
                         + " " + culture.DateTimeFormat.LongTimePattern;

        // Normalize to tabular pattern
        pattern = PadDateTimePattern(pattern);

        // Format the DateTime
        string result = dateTime?.ToString(pattern, culture);

        // Remove RTL marks that might appear in RTL cultures
        result = RemoveRtlMarks(result);

        return result;
    }

    /// <summary>
    /// Formats a TimeOnly using the given culture's date/time format,
    /// but ensures zero-padding for consistent column width.
    /// </summary>
    public static string Format(TimeOnly? dateTime, CultureInfo culture)
    {
        if (dateTime is null)
            return string.Empty;

        // Build combined pattern from ShortDate and LongTime patterns
        string pattern = culture.DateTimeFormat.LongTimePattern;

        // Normalize to tabular pattern
        pattern = PadDateTimePattern(pattern);

        // Format the DateTime
        string result = dateTime?.ToString(pattern, culture);

        // Remove RTL marks that might appear in RTL cultures
        result = RemoveRtlMarks(result);

        return result;
    }

    /// <summary>
    /// Replaces single-character date/time format tokens (d, M, H, h, m, s)
    /// with their padded versions (dd, MM, etc.), leaving multi-letter tokens intact.
    /// </summary>
    private static string PadDateTimePattern(string pattern)
    {
        pattern = RE_Replace_dd().Replace(pattern, "dd");
        pattern = RE_Replace_MM().Replace(pattern, "MM");
        pattern = RE_Replace_HH().Replace(pattern, "HH");
        pattern = RE_Replace_hh().Replace(pattern, "hh");
        pattern = RE_Replace_mm().Replace(pattern, "mm");
        pattern = RE_Replace_ss().Replace(pattern, "ss");

        return pattern;
    }

    /// <summary>
    /// Removes invisible Right-to-Left and Left-to-Right marks that can affect layout.
    /// </summary>
    private static string RemoveRtlMarks(string input)
    {
        if (string.IsNullOrEmpty(input))
            return input;
        
        return input
            .Replace("\u200F", "")
            .Replace("\u200E", "");
    }

    [GeneratedRegex(@"(?<!d)d(?!d)")]
    private static partial Regex RE_Replace_dd();
    [GeneratedRegex(@"(?<!M)M(?!M)")]
    private static partial Regex RE_Replace_MM();
    [GeneratedRegex(@"(?<!H)H(?!H)")]
    private static partial Regex RE_Replace_HH();
    [GeneratedRegex(@"(?<!h)h(?!h)")]
    private static partial Regex RE_Replace_hh();
    [GeneratedRegex(@"(?<!m)m(?!m)")]
    private static partial Regex RE_Replace_mm();
    [GeneratedRegex(@"(?<!s)s(?!s)")]
    private static partial Regex RE_Replace_ss();
}
