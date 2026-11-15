namespace ADB_Explorer.Helpers;

/// <summary>
/// Device file names for targets that refuse some of them, as rclone does: what a target refuses becomes a lookalike
/// (a fullwidth form, or a control picture). <see cref="ToWindows"/> and <see cref="ToDevice"/> map both ways, for the
/// mount; the <c>For…</c> substitutions are one way, for copies, so lookalikes a name already has stay as they are.
/// </summary>
public static class FileNameEncoding
{
    /// <summary>Marks a lookalike that is the device name's own, not a stand-in.</summary>
    private const char ESCAPE = '‛'; // ‛

    // '!'..'~' map to their fullwidth forms U+FF01..U+FF5E
    private const int FULLWIDTH_OFFSET = 0xFEE0;

    private const char FULLWIDTH_FIRST = '！';

    private const char FULLWIDTH_LAST = '～';

    // Control characters 0x01..0x1F map to their pictures U+2401..U+241F
    private const char CONTROL_PICTURES = '␀';

    private const char SPACE_PICTURE = '␠'; // ␠

    private const int MAX_NAME_LENGTH = 255;

    private const string INVALID_CHARS = "<>:\"|?*\\";

    private static readonly string[] RESERVED_NAMES =
        ["CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "COM¹", "COM²", "COM³",
         "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9", "LPT¹", "LPT²", "LPT³"];

    private static bool IsLookalike(char c)
        => c is >= FULLWIDTH_FIRST and <= FULLWIDTH_LAST or >= CONTROL_PICTURES and <= SPACE_PICTURE or ESCAPE;

    private static char Fullwidth(char c) => (char)(c + FULLWIDTH_OFFSET);

    /// <summary>The Windows name for a device name, mapped back by <see cref="ToDevice"/>; null when too long for Windows.</summary>
    public static string? ToWindows(string deviceName) => MapForWindows(deviceName, escape: true);

    /// <summary>The name a copy to Windows gets: lookalikes for what Windows refuses. Unchanged when even that is too long.</summary>
    public static string ForWindows(string deviceName) => MapForWindows(deviceName, escape: false) ?? deviceName;

    /// <summary>The name a copy to a drive with restricted naming (FUSE) gets: lookalikes for the characters it refuses.</summary>
    public static string ForRestrictedNaming(string deviceName)
        => new([.. deviceName.Select(c => INVALID_CHARS.Contains(c) ? Fullwidth(c) : c)]);

    private static string? MapForWindows(string deviceName, bool escape)
    {
        if (deviceName.Length == 0)
            return null;

        var name = new StringBuilder(deviceName.Length);
        foreach (var c in deviceName)
        {
            if (escape && IsLookalike(c))
                name.Append(ESCAPE).Append(c);
            else if (c < ' ')
                name.Append((char)(CONTROL_PICTURES + c));
            else if (INVALID_CHARS.Contains(c))
                name.Append(Fullwidth(c));
            else
                name.Append(c);
        }

        // Windows drops a trailing dot or space
        if (name[^1] is '.')
            name[^1] = Fullwidth('.');
        else if (name[^1] is ' ')
            name[^1] = SPACE_PICTURE;

        // A device name like CON.txt, whatever its case: the first letter stands in
        var stem = name.ToString().Split('.')[0].TrimEnd(' ');
        if (RESERVED_NAMES.Contains(stem, StringComparer.OrdinalIgnoreCase))
            name[0] = Fullwidth(name[0]);

        if (name.Length > MAX_NAME_LENGTH)
            return null;

        return name.ToString();
    }

    /// <summary>Whether Windows accepts the device name as it is.</summary>
    public static bool IsValidWindowsName(string name)
    {
        if (name.Length is 0 or > MAX_NAME_LENGTH || name.IndexOfAny([.. INVALID_CHARS]) >= 0 || name.Any(c => c < ' '))
            return false;

        if (name.EndsWith('.') || name.EndsWith(' '))
            return false;

        return !RESERVED_NAMES.Contains(name.Split('.')[0].TrimEnd(' '), StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The device name for a Windows name, from <see cref="ToWindows"/> or typed in Windows.</summary>
    public static string ToDevice(string windowsName)
    {
        var name = new StringBuilder(windowsName.Length);
        for (var i = 0; i < windowsName.Length; i++)
        {
            var c = windowsName[i];
            if (c is ESCAPE && i + 1 < windowsName.Length)
                name.Append(windowsName[++i]);
            else if (c is >= FULLWIDTH_FIRST and <= FULLWIDTH_LAST)
                name.Append((char)(c - FULLWIDTH_OFFSET));
            else if (c is > CONTROL_PICTURES and < SPACE_PICTURE)
                name.Append((char)(c - CONTROL_PICTURES));
            else if (c is SPACE_PICTURE)
                name.Append(' ');
            else
                name.Append(c);
        }

        return name.ToString();
    }

    /// <summary>
    /// Spellings of a Windows name that Windows tells apart from it, for a device name differing from another only in case:
    /// its uppercase letters, then its lowercase ones, then all of them in fullwidth form. Each maps back to the same device name.
    /// </summary>
    public static IEnumerable<string> CaseVariants(string windowsName)
    {
        foreach (var convert in new Func<char, bool>[] { char.IsAsciiLetterUpper, char.IsAsciiLetterLower, char.IsAsciiLetter })
        {
            var variant = new string([.. windowsName.Select(c => convert(c) ? Fullwidth(c) : c)]);
            if (variant != windowsName)
                yield return variant;
        }
    }

    /// <summary>A relative path with <paramref name="substitute"/> applied to each of its names.</summary>
    public static string ForPath(string relativePath, char separator, Func<string, string> substitute)
        => string.Join(separator, relativePath.Split(separator).Select(name => name.Length == 0 ? name : substitute(name)));
}
