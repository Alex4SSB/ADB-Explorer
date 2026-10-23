namespace ADB_Explorer.Services;

public static partial class NativeMethods
{
    /// <summary>
    /// CLDR plural rules from the ICU that ships with Windows 10 1903+ (the app's minimum OS).
    /// </summary>
    public static partial class PluralRules
    {
        public const string One = "one";
        public const string Other = "other";

        private const int KeywordCapacity = 16;

        private static readonly ConcurrentDictionary<string, nint> _rules = new();

        /// <summary>Returns the CLDR category (zero, one, two, few, many or other) of <paramref name="count"/>.</summary>
        public static unsafe string Select(CultureInfo culture, long count)
        {
            var rules = GetRules(culture);
            if (rules == 0)
                return count == 1 ? One : Other;

            int status = 0;
            char* keyword = stackalloc char[KeywordCapacity];
            int length = uplrules_select(rules, count, keyword, KeywordCapacity, ref status);
            if (status > 0 || length <= 0)
                return Other;

            return new string(keyword, 0, length);
        }

        /// <summary>Returns every CLDR category the language of <paramref name="culture"/> distinguishes.</summary>
        public static unsafe IReadOnlyList<string> Categories(CultureInfo culture)
        {
            var rules = GetRules(culture);
            if (rules == 0)
                return [One, Other];

            int status = 0;
            var keywords = uplrules_getKeywords(rules, ref status);
            if (status > 0)
                return [Other];

            try
            {
                List<string> result = [];
                byte* keyword;
                while ((keyword = uenum_next(keywords, out int length, ref status)) is not null && status <= 0)
                {
                    result.Add(Encoding.ASCII.GetString(keyword, length));
                }

                return result;
            }
            finally
            {
                uenum_close(keywords);
            }
        }

        // ICU locale IDs separate the region with an underscore (pt_BR)
        private static nint GetRules(CultureInfo culture)
            => _rules.GetOrAdd(culture.Name.Replace('-', '_'), Open);

        private static nint Open(string locale)
        {
            try
            {
                int status = 0;
                var rules = uplrules_open(locale, ref status);

                return status > 0 ? 0 : rules;
            }
            catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
            {
                return 0;
            }
        }

        // ICU status codes: > 0 is a failure, <= 0 is success or a warning
        [LibraryImport("icu.dll", StringMarshalling = StringMarshalling.Utf8)]
        private static partial nint uplrules_open(string locale, ref int status);

        [LibraryImport("icu.dll")]
        private static unsafe partial int uplrules_select(nint rules, double number, char* keyword, int capacity, ref int status);

        [LibraryImport("icu.dll")]
        private static partial nint uplrules_getKeywords(nint rules, ref int status);

        [LibraryImport("icu.dll")]
        private static unsafe partial byte* uenum_next(nint enumeration, out int length, ref int status);

        [LibraryImport("icu.dll")]
        private static partial void uenum_close(nint enumeration);
    }
}
