namespace ADB_Explorer.Platform.Linux;

/// <summary>
/// Finds the default file manager's name in a given language: its <c>.desktop</c> file's <c>Name[locale]=</c>,
/// then the distribution's gettext translation (Ubuntu keeps names only there), else <see langword="null"/>.
/// </summary>
public sealed class FileManagerNameLookup(IReadOnlyList<string> dataDirs, IReadOnlyList<string> localeDirs, Func<string?> queryDesktopId)
{
    private readonly Lazy<DesktopEntry?> _entry = new(() => FindEntry(dataDirs, queryDesktopId()));

    private readonly ConcurrentDictionary<string, string?> _names = new();

    /// <summary>The lookup for this machine, using the XDG folders and <c>xdg-mime</c>.</summary>
    public static FileManagerNameLookup ForCurrentSystem()
        => new(XdgDataDirs(), ["/usr/share/locale-langpack", "/usr/share/locale"], QueryDefaultDirectoryHandler);

    public string? GetName(CultureInfo culture) => _names.GetOrAdd(culture.Name, _ => Find(culture));

    private string? Find(CultureInfo culture)
    {
        if (_entry.Value is not { } entry)
            return null;

        var locales = LocaleNames(culture);
        if (entry.LocalizedName(locales) is { } localized)
            return localized;

        if (entry.Name is not { Length: > 0 } name)
            return null;

        if (IsEnglish(culture))
            return name;

        if (entry.GettextDomain is not { Length: > 0 } domain)
            return null;

        foreach (var locale in locales)
        {
            foreach (var dir in localeDirs)
            {
                var translation = MoCatalog.Translate(Path.Combine(dir, locale, "LC_MESSAGES", $"{domain}.mo"), name);
                if (translation is not null)
                    return translation;
            }
        }

        return null;
    }

    // Name= is the untranslated (English) name, so it only answers for English
    private static bool IsEnglish(CultureInfo culture) => culture.TwoLetterISOLanguageName == "en";

    /// <summary>The locale names to try for a culture, most specific first: pt-BR gives pt_BR, then pt.</summary>
    public static IReadOnlyList<string> LocaleNames(CultureInfo culture)
    {
        var parts = culture.Name.Split('-');
        var language = parts[0];
        if (parts.Length > 1 && parts[^1].Length == 2)
            return [$"{language}_{parts[^1]}", language];

        return [language];
    }

    internal static DesktopEntry? FindEntry(IEnumerable<string> dataDirs, string? desktopId)
    {
        if (string.IsNullOrWhiteSpace(desktopId))
            return null;

        // A "vendor-name.desktop" ID can also live at "applications/vendor/name.desktop"
        string[] relativePaths = [desktopId];
        var dash = desktopId.IndexOf('-');
        if (dash > 0)
            relativePaths = [desktopId, $"{desktopId[..dash]}/{desktopId[(dash + 1)..]}"];

        foreach (var dir in dataDirs)
        {
            foreach (var relative in relativePaths)
            {
                var path = Path.Combine(dir, "applications", relative);
                if (File.Exists(path))
                    return DesktopEntry.Load(path);
            }
        }

        return null;
    }

    /// <summary>$XDG_DATA_HOME, then $XDG_DATA_DIRS, with the spec's defaults.</summary>
    private static IReadOnlyList<string> XdgDataDirs()
    {
        var home = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrEmpty(home))
            home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");

        var dirs = Environment.GetEnvironmentVariable("XDG_DATA_DIRS");
        if (string.IsNullOrEmpty(dirs))
            dirs = "/usr/local/share:/usr/share";

        return [home, .. dirs.Split(':', StringSplitOptions.RemoveEmptyEntries)];
    }

    private static string? QueryDefaultDirectoryHandler()
    {
        try
        {
            ProcessStartInfo info = new("xdg-mime") { RedirectStandardOutput = true, UseShellExecute = false };
            info.ArgumentList.Add("query");
            info.ArgumentList.Add("default");
            info.ArgumentList.Add("inode/directory");

            using var process = Process.Start(info);
            if (process is null)
                return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            if (!process.WaitForExit(2000) || process.ExitCode != 0)
                return null;

            return output.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim();
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return null;
        }
    }
}
