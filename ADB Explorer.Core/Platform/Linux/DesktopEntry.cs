namespace ADB_Explorer.Platform.Linux;

/// <summary>The <c>[Desktop Entry]</c> group of a freedesktop <c>.desktop</c> file.</summary>
public sealed class DesktopEntry
{
    private const string MainGroup = "[Desktop Entry]";

    private readonly Dictionary<string, string> _keys;

    private DesktopEntry(Dictionary<string, string> keys) => _keys = keys;

    public string? Name => Get("Name");

    /// <summary>
    /// The gettext domain distributions use instead of <c>Name[xx]=</c> lines;
    /// Ubuntu removes those lines and adds <c>X-Ubuntu-Gettext-Domain</c>.
    /// </summary>
    public string? GettextDomain => Get("X-Ubuntu-Gettext-Domain") ?? Get("X-GNOME-Gettext-Domain");

    public string? Get(string key) => _keys.GetValueOrDefault(key);

    /// <summary>The first <c>Name[locale]=</c> value for the locales, in order.</summary>
    public string? LocalizedName(IEnumerable<string> locales)
    {
        foreach (var locale in locales)
        {
            if (Get($"Name[{locale}]") is { Length: > 0 } name)
                return name;
        }

        return null;
    }

    public static DesktopEntry? Load(string path)
    {
        try
        {
            return Parse(File.ReadLines(path));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static DesktopEntry Parse(IEnumerable<string> lines)
    {
        Dictionary<string, string> keys = [];
        var inMainGroup = false;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.Length == 0 || line[0] == '#')
                continue;

            if (line[0] == '[')
            {
                inMainGroup = line == MainGroup;
                continue;
            }

            var separator = line.IndexOf('=');
            if (!inMainGroup || separator <= 0)
                continue;

            keys.TryAdd(line[..separator].TrimEnd(), Unescape(line[(separator + 1)..].TrimStart()));
        }

        return new(keys);
    }

    // The escapes the Desktop Entry spec defines for string values
    private static string Unescape(string value)
    {
        if (!value.Contains('\\'))
            return value;

        StringBuilder result = new(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] != '\\' || i + 1 == value.Length)
            {
                result.Append(value[i]);
                continue;
            }

            i++;
            result.Append(value[i] switch
            {
                's' => ' ',
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                _ => value[i],
            });
        }

        return result.ToString();
    }
}
