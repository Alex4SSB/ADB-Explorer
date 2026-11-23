namespace ADB_Explorer.Strings;

/// <summary>
/// CLDR plural rules (CLDR 42+) for whole numbers, for the languages the app is translated to.
/// A new translation language needs a rule here, or it falls back to English rules.
/// </summary>
public static class PluralRules
{
    public const string Zero = "zero";
    public const string One = "one";
    public const string Two = "two";
    public const string Few = "few";
    public const string Many = "many";
    public const string Other = "other";

    private sealed record Rule(string[] Categories, Func<long, string> Select);

    private static readonly Rule _otherOnly = new([Other], _ => Other);

    private static readonly Rule _oneIsOne = new([One, Other], n => n == 1 ? One : Other);

    private static readonly Rule _zeroOrOneIsOne = new([One, Other], n => n is 0 or 1 ? One : Other);

    // Romance languages: millions take the "many" form ("de millions")
    private static readonly Rule _romance = new([One, Many, Other], n => n == 1 ? One : Million(n));

    private static readonly Rule _romanceZeroOrOne = new([One, Many, Other], n => n is 0 or 1 ? One : Million(n));

    private static readonly Rule _eastSlavic = new([One, Few, Many, Other], n =>
    {
        long mod10 = n % 10, mod100 = n % 100;
        if (mod10 == 1 && mod100 != 11)
            return One;

        if (mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14)
            return Few;

        return Many;
    });

    private static readonly Dictionary<string, Rule> _rules = new()
    {
        ["ar"] = new([Zero, One, Two, Few, Many, Other], n =>
        {
            long mod100 = n % 100;
            if (n is 0)
                return Zero;

            if (n is 1)
                return One;

            if (n is 2)
                return Two;

            if (mod100 is >= 3 and <= 10)
                return Few;

            return mod100 >= 11 ? Many : Other;
        }),
        ["bn"] = _zeroOrOneIsOne,
        ["cs"] = new([One, Few, Many, Other], n =>
        {
            if (n is 1)
                return One;

            return n is >= 2 and <= 4 ? Few : Other;
        }),
        ["de"] = _oneIsOne,
        ["en"] = _oneIsOne,
        ["es"] = _romance,
        ["fa"] = _zeroOrOneIsOne,
        ["fr"] = _romanceZeroOrOne,
        ["he"] = new([One, Two, Other], n =>
        {
            if (n is 1)
                return One;

            return n is 2 ? Two : Other;
        }),
        ["id"] = _otherOnly,
        ["it"] = _romance,
        ["ja"] = _otherOnly,
        ["ko"] = _otherOnly,
        ["ms"] = _otherOnly,
        ["nl"] = _oneIsOne,
        ["pl"] = new([One, Few, Many, Other], n =>
        {
            long mod10 = n % 10, mod100 = n % 100;
            if (n is 1)
                return One;

            return mod10 is >= 2 and <= 4 && mod100 is < 12 or > 14 ? Few : Many;
        }),
        ["pt"] = _romanceZeroOrOne,
        ["pt-PT"] = _romance,
        ["ro"] = new([One, Few, Other], n =>
        {
            if (n is 1)
                return One;

            return n is 0 || n % 100 is >= 1 and <= 19 ? Few : Other;
        }),
        ["ru"] = _eastSlavic,
        ["sv"] = _oneIsOne,
        ["ta"] = _oneIsOne,
        ["tr"] = _oneIsOne,
        ["uk"] = _eastSlavic,
        ["vi"] = _otherOnly,
        ["zh"] = _otherOnly,
    };

    private static readonly ConcurrentDictionary<string, IReadOnlyList<string>> _wholeNumberCategories = new();

    /// <summary>Returns the CLDR category (zero, one, two, few, many or other) of <paramref name="count"/>.</summary>
    public static string Select(CultureInfo culture, long count) => GetRule(culture).Select(Math.Abs(count));

    /// <summary>Returns every CLDR category the language of <paramref name="culture"/> distinguishes.</summary>
    public static IReadOnlyList<string> Categories(CultureInfo culture) => GetRule(culture).Categories;

    /// <summary>
    /// The categories whole numbers fall into - the forms Weblate asks for. CLDR integer rules repeat
    /// within 0-999, and some categories only hold fractions (ru "other").
    /// </summary>
    public static IReadOnlyList<string> WholeNumberCategories(CultureInfo culture)
        => _wholeNumberCategories.GetOrAdd(culture.Name,
            _ => [.. Enumerable.Range(0, 1000).Select(n => Select(culture, n)).Distinct()]);

    private static string Million(long n) => n != 0 && n % 1_000_000 == 0 ? Many : Other;

    /// <summary>The rule for the culture's full name (pt-PT), then its language (pt), else English.</summary>
    private static Rule GetRule(CultureInfo culture)
    {
        if (_rules.TryGetValue(culture.Name, out var rule))
            return rule;

        var language = culture.Name.Split('-')[0];

        return _rules.GetValueOrDefault(language, _oneIsOne);
    }
}
