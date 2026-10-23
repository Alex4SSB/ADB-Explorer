namespace ADB_Explorer.Strings;

/// <summary>
/// Count-dependent strings, translated in Weblate as i18next v4 JSON (<c>Strings/Plurals/*.json</c>).
/// Each method name is its key, and the form is picked by the CLDR plural rules of the file's language.
/// </summary>
public static class Plurals
{
    public static string Items(long? count) => Format(nameof(Items), count);

    public static string ItemsSelected(long count) => Format(nameof(ItemsSelected), count);

    public static string Files(long count) => Format(nameof(Files), count);

    public static string Folders(long count) => Format(nameof(Folders), count);

    public static string ConflictingItems(long count) => Format(nameof(ConflictingItems), count);

    public static string ConflictingItemsIn(long count, string destination)
        => Format(nameof(ConflictingItemsIn), count, ("destination", destination));

    public static string PackagesToRemove(long count) => Format(nameof(PackagesToRemove), count);

    public static string InstallApks(long count) => Format(nameof(InstallApks), count);

    public static string PasteCutItems(long count) => Format(nameof(PasteCutItems), count);

    public static string PasteCopiedItems(long count) => Format(nameof(PasteCopiedItems), count);

    public static string CopyItems(long count) => Format(nameof(CopyItems), count);

    public static string CopyItemsTo(long count, string target) => Format(nameof(CopyItemsTo), count, ("target", target));

    public static string MoveItems(long count) => Format(nameof(MoveItems), count);

    public static string MoveItemsTo(long count, string target) => Format(nameof(MoveItemsTo), count, ("target", target));

    public static string FilesInvalidAtDriveRoot(long count) => Format(nameof(FilesInvalidAtDriveRoot), count);

    public static string ApkInstallers(long? count) => Format(nameof(ApkInstallers), count);

    public static string Packages(long? count) => Format(nameof(Packages), count);

    public static string RemoveOperations(long count) => Format(nameof(RemoveOperations), count);

    public static string ValidateOperations(long count) => Format(nameof(ValidateOperations), count);

    public static string RetryOperations(long count) => Format(nameof(RetryOperations), count);

    internal const string SourceLanguage = "en";

    // Weblate has no "other" form for one/few/many languages (ru, uk, pl).
    // zero/one/two may spell out their number, so they never stand in for another count.
    private static readonly string[] _fallbackCategories = [NativeMethods.PluralRules.Other, "many", "few"];

    private const string ResourcePrefix = "ADB_Explorer.Strings.Plurals.";

    private static readonly ConcurrentDictionary<string, IReadOnlyDictionary<string, string>?> _tables = new();

    /// <summary>An unknown (<see langword="null"/>) count is shown as ?? with the <c>other</c> form.</summary>
    private static string Format(string key, long? count, params (string Name, string Value)[] args)
    {
        var text = Lookup(Resources.Culture ?? CultureInfo.GetCultureInfo(SourceLanguage), key, count)
            .Replace("{{count}}", count?.ToString() ?? "??");

        foreach (var (name, value) in args)
        {
            text = text.Replace($"{{{{{name}}}}}", value);
        }

        return text;
    }

    /// <summary>
    /// Walks the culture's fallback chain down to the source language, and returns the first file's form
    /// for <paramref name="count"/>, or a general form when the needed one isn't translated yet.
    /// </summary>
    internal static string Lookup(CultureInfo culture, string key, long? count)
    {
        foreach (var language in FallbackChain(culture))
        {
            if (Table(language.Name) is not { } table)
                continue;

            var category = NativeMethods.PluralRules.Other;
            if (count is { } n)
                category = NativeMethods.PluralRules.Select(language, n);

            if (table.TryGetValue($"{key}_{category}", out var text) && text != "")
                return text;

            foreach (var fallback in _fallbackCategories)
            {
                if (table.TryGetValue($"{key}_{fallback}", out text) && text != "")
                    return text;
            }
        }

        return key;
    }

    /// <summary>
    /// Counts the source strings, and how many of them <paramref name="culture"/>'s own file translates
    /// in every form whole numbers need in its language.
    /// </summary>
    internal static (int Translated, int Total) TranslationProgress(CultureInfo culture)
    {
        var keys = Keys();
        var table = Table(culture.Name);
        if (table is null)
            return (0, keys.Count);

        var categories = NativeMethods.PluralRules.WholeNumberCategories(culture);
        var translated = keys.Count(key => categories.All(category =>
            table.TryGetValue($"{key}_{category}", out var text) && text != ""));

        return (translated, keys.Count);
    }

    /// <summary>The source keys, without their plural suffix.</summary>
    internal static IReadOnlyCollection<string> Keys()
        => Table(SourceLanguage)?.Keys.Select(BaseKey).ToHashSet() ?? [];

    internal static string BaseKey(string key) => key[..key.LastIndexOf('_')];

    internal static IReadOnlyDictionary<string, string>? Table(string language) => _tables.GetOrAdd(language, Load);

    /// <summary>The languages that have a plurals file.</summary>
    internal static IEnumerable<string> Languages()
        => typeof(Plurals).Assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(ResourcePrefix))
            .Select(name => name[ResourcePrefix.Length..^".json".Length]);

    private static IEnumerable<CultureInfo> FallbackChain(CultureInfo culture)
    {
        for (var c = culture; !c.Equals(CultureInfo.InvariantCulture); c = c.Parent)
        {
            yield return c;
        }

        yield return CultureInfo.GetCultureInfo(SourceLanguage);
    }

    private static IReadOnlyDictionary<string, string>? Load(string language)
    {
        using var stream = typeof(Plurals).Assembly.GetManifestResourceStream($"{ResourcePrefix}{language}.json");
        if (stream is null)
            return null;

        return JsonSerializer.Deserialize<Dictionary<string, string>>(stream);
    }
}
