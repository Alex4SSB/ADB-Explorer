namespace ADB_Explorer.Services;

/// <summary>
/// Minimal resources.arsc reader that handles sparse/offset16 type chunks.
/// AlphaOmega's <c>ResourceMap</c> collapses configs and often maps the wrong string
/// for sparse packages.
/// </summary>
internal static class ArscResourceResolver
{
    private const ushort ResStringPoolType = 0x0001;
    private const ushort ResTablePackageType = 0x0200;
    private const ushort ResTableTypeType = 0x0201;
    private const byte FlagSparse = 0x01;
    private const byte FlagOffset16 = 0x02;
    private const ushort EntryFlagComplex = 0x0001;
    private const byte DataTypeReference = 0x01;
    private const byte DataTypeString = 0x03;
    private const byte DataTypeColorArgb8 = 0x1C;
    private const byte DataTypeColorRgb8 = 0x1D;
    private const byte DataTypeColorArgb4 = 0x1E;
    private const byte DataTypeColorRgb4 = 0x1F;

    private readonly record struct ConfigValue(string Language, string Country, byte DataType, int Data, string? StringValue);

    public static string? ResolveString(byte[] arscBytes, int resourceId)
        => ResolveString(arscBytes, resourceId, preferredCulture: null);

    /// <param name="preferredCulture">
    /// UI culture to prefer. When null, falls back to English / default / any (legacy behavior).
    /// </param>
    public static string? ResolveString(byte[] arscBytes, int resourceId, CultureInfo? preferredCulture)
    {
        var values = ResolveValues(arscBytes, resourceId, maxReferenceDepth: 3);
        var strings = values
            .Where(v => v.DataType == DataTypeString && !string.IsNullOrWhiteSpace(v.StringValue))
            .Select(v => (v.Language, v.Country, v.StringValue!))
            .ToList();

        return PickPreferredString(strings, preferredCulture);
    }

    /// <summary>Android language codes that correspond to a .NET UI culture (e.g. <c>he</c> → <c>iw</c>).</summary>
    public static IReadOnlyList<string> AndroidLanguageTagsFor(CultureInfo culture)
    {
        var lang = culture.TwoLetterISOLanguageName;
        if (string.IsNullOrEmpty(lang) || lang.Equals("iv", StringComparison.OrdinalIgnoreCase))
            return ["en"];

        lang = lang.ToLowerInvariant();
        var country = culture.Name.Contains('-', StringComparison.Ordinal)
            ? culture.Name[(culture.Name.IndexOf('-') + 1)..].ToUpperInvariant()
            : "";

        // Android still uses legacy ISO codes for some languages.
        var androidLang = lang switch
        {
            "he" => "iw",
            "id" => "in",
            "yi" => "ji",
            _ => lang,
        };

        var tags = new List<string>();
        if (!string.IsNullOrEmpty(country))
        {
            tags.Add($"{androidLang}-{country}");
            if (androidLang != lang)
                tags.Add($"{lang}-{country}");
        }

        tags.Add(androidLang);
        if (androidLang != lang)
            tags.Add(lang);

        return tags;
    }

    /// <summary>Resolves drawable/mipmap paths (and follows references).</summary>
    public static List<string> ResolvePaths(byte[] arscBytes, int resourceId)
    {
        var values = ResolveValues(arscBytes, resourceId, maxReferenceDepth: 5);
        return values
            .Where(v => v.DataType == DataTypeString
                        && !string.IsNullOrWhiteSpace(v.StringValue)
                        && IsDrawablePath(v.StringValue))
            .Select(v => v.StringValue!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsDrawablePath(string value)
    {
        if (value.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
            return true;

        // Obfuscated root-level resource names: short, no spaces, no package-like dots.
        if (value.Length is < 1 or > 64)
            return false;
        if (value.Contains(' ', StringComparison.Ordinal) || value.Contains('\\'))
            return false;
        if (value.Contains('.', StringComparison.Ordinal)
            && !value.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && !value.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
            && !value.EndsWith(".webp", StringComparison.OrdinalIgnoreCase)
            && !value.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase))
            return false;

        return value.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.' or '/');
    }

    public static uint? ResolveColor(byte[] arscBytes, int resourceId)
    {
        var values = ResolveValues(arscBytes, resourceId, maxReferenceDepth: 3);
        foreach (var value in PreferDefaultConfig(values))
        {
            if (value.DataType is DataTypeColorArgb8 or DataTypeColorRgb8 or DataTypeColorArgb4 or DataTypeColorRgb4)
                return unchecked((uint)value.Data);
        }

        return null;
    }

    private static string? PickPreferredString(
        IReadOnlyList<(string Language, string Country, string Value)> values,
        CultureInfo? preferredCulture)
    {
        if (values.Count == 0)
            return null;

        // Drop Android pseudo-locales (en-XA accented / en-XB bidi) — they match "en" otherwise.
        values = values.Where(v => !IsPseudoLocale(v.Language, v.Country)).ToList();
        if (values.Count == 0)
            return null;

        // 1) Exact / aliased match for the app UI culture — do not require Latin script
        //    (Hebrew, Arabic, CJK, … must win when that is the active UI language).
        if (preferredCulture is not null)
        {
            var tags = AndroidLanguageTagsFor(preferredCulture);
            foreach (var tag in tags)
            {
                var dash = tag.IndexOf('-');
                var wantLang = dash < 0 ? tag : tag[..dash];
                var wantCountry = dash < 0 ? "" : tag[(dash + 1)..];

                var matched = values
                    .Where(v =>
                    {
                        if (!v.Language.Equals(wantLang, StringComparison.OrdinalIgnoreCase))
                            return false;
                        if (string.IsNullOrEmpty(wantCountry))
                            return true;
                        return v.Country.Equals(wantCountry, StringComparison.OrdinalIgnoreCase);
                    })
                    .OrderBy(v => string.IsNullOrEmpty(v.Country) ? 0 : 1) // bare "en" before "en-GB"
                    .Select(v => v.Value)
                    .Where(IsPlausible)
                    .ToList();

                if (matched.Count > 0)
                    return MajorityString(matched);
            }
        }

        // 2) English / default / any — prefer Latin to avoid polluted non-default entries
        //    when the UI culture has no matching resource.
        foreach (var predicate in new Func<(string Language, string Country, string Value), bool>[]
                 {
                     v => v.Language.Equals("en", StringComparison.OrdinalIgnoreCase),
                     v => string.IsNullOrEmpty(v.Language),
                     _ => true,
                 })
        {
            var matched = values
                .Where(predicate)
                .OrderBy(v => string.IsNullOrEmpty(v.Country) ? 0 : 1)
                .Select(v => v.Value)
                .Where(IsPlausible)
                .ToList();
            var latin = matched.Where(IsMostlyLatin).ToList();
            if (latin.Count == 0)
                continue;

            return MajorityString(latin);
        }

        // 3) No Latin candidate — last resort any plausible string.
        var any = values.Select(v => v.Value).Where(IsPlausible).ToList();
        return any.Count == 0 ? null : MajorityString(any);
    }

    private static string MajorityString(IReadOnlyList<string> values)
        => values
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Length)
            .Select(g => g.Key)
            .First();

    /// <summary>Android pseudo-locales used for UI testing (<c>en-XA</c> accents, <c>en-XB</c> bidi).</summary>
    private static bool IsPseudoLocale(string language, string country)
        => country.Equals("XA", StringComparison.OrdinalIgnoreCase)
           || country.Equals("XB", StringComparison.OrdinalIgnoreCase);

    private static bool IsPlausible(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.Length is < 1 or > 80)
            return false;
        if (value.Contains('%', StringComparison.Ordinal))
            return false;
        if (value.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
            return false;

        // en-XA / en-XB pseudo strings: "[Nämé one two three]" or "[Nämé one two]"
        if (IsPseudoAccentLabel(value))
            return false;

        // Obfuscated single-token junk (e.g. "ab"), not short real labels.
        if (value.Length <= 2)
            return false;

        // Typed-value / pool-index mistakes (GoogleExtShared → "65536").
        if (value.Length <= 8 && value.All(char.IsAsciiDigit))
            return false;

        var parts = value.Split('.');
        if (parts.Length >= 3
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '$')
            && parts[0] is "com" or "org" or "net" or "io" or "android" or "java" or "kotlin")
            return false;

        if (value.Contains("android.", StringComparison.OrdinalIgnoreCase)
            && value.Contains('.', StringComparison.Ordinal)
            && !value.Contains(' ', StringComparison.Ordinal))
            return false;

        // Do not reject PascalCase tokens here — many system/overlay apps use the
        // resource name as the English label (e.g. SetupWizardOverlay). Rejecting those
        // caused fallback to an arbitrary localized string (Estonian for that overlay).

        return true;
    }

    /// <summary>Detects Android pseudo-accent / pseudo-bidi label strings.</summary>
    internal static bool IsPseudoAccentLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        // Typical en-XA: ends with " one two three" or " one two", often wrapped in [].
        if (value.Contains(" one two three", StringComparison.OrdinalIgnoreCase)
            || value.Contains(" one two", StringComparison.OrdinalIgnoreCase))
            return true;

        if (value.Length >= 3 && value[0] == '[' && value[^1] == ']'
            && value.Contains(" one", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsMostlyLatin(string value)
    {
        var letters = 0;
        var latin = 0;
        foreach (var c in value)
        {
            if (!char.IsLetter(c))
                continue;
            letters++;
            if (c <= 0x024F)
                latin++;
        }

        return letters == 0 || latin * 2 >= letters;
    }

    private static IEnumerable<ConfigValue> PreferDefaultConfig(IEnumerable<ConfigValue> values)
        => values
            .OrderBy(v => string.IsNullOrEmpty(v.Language) ? 0 : v.Language.Equals("en", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(v => v.Country.Length);

    private static List<ConfigValue> ResolveValues(byte[] data, int resourceId, int maxReferenceDepth)
    {
        if (data.Length < 12 || maxReferenceDepth < 0)
            return [];

        try
        {
            if (!TryParsePackages(data, out var valuePool, out var packages))
                return [];

            var typeId = (resourceId >> 16) & 0xFF;
            var entryIndex = resourceId & 0xFFFF;
            var packageId = (resourceId >> 24) & 0xFF;

            var result = new List<ConfigValue>();
            var pendingRefs = new List<int>();

            foreach (var package in packages)
            {
                if (package.Id != packageId)
                    continue;

                foreach (var chunk in package.TypeChunks)
                {
                    if (chunk.TypeId != typeId)
                        continue;

                    foreach (var (index, entryOffset) in EnumerateEntryOffsets(data, chunk))
                    {
                        if (index != entryIndex)
                            continue;

                        if (!TryReadSimpleValue(data, chunk.ChunkPos + chunk.EntriesStart + entryOffset, out var dataType, out var payload))
                            continue;

                        if (dataType == DataTypeReference)
                        {
                            pendingRefs.Add(payload);
                            continue;
                        }

                        string? str = null;
                        if (dataType == DataTypeString && payload >= 0 && payload < valuePool.Length)
                            str = valuePool[payload];

                        result.Add(new ConfigValue(chunk.Language, chunk.Country, dataType, payload, str));
                    }
                }
            }

            if (result.Count > 0)
                return result;

            // Follow references only when this id itself had no concrete values.
            foreach (var referenced in pendingRefs.Distinct())
                result.AddRange(ResolveValues(data, referenced, maxReferenceDepth - 1));

            return result;
        }
        catch
        {
            return [];
        }
    }

    private static bool TryParsePackages(byte[] data, out string[] valuePool, out List<PackageData> packages)
    {
        valuePool = [];
        packages = [];

        var tableHeaderSize = BitConverter.ToUInt16(data, 2);
        if (tableHeaderSize < 8 || tableHeaderSize >= data.Length)
            return false;

        valuePool = ReadStringPool(data, tableHeaderSize);
        var globalPoolSize = BitConverter.ToInt32(data, tableHeaderSize + 4);
        var pos = tableHeaderSize + globalPoolSize;

        while (pos + 8 <= data.Length)
        {
            var type = BitConverter.ToUInt16(data, pos);
            var headerSize = BitConverter.ToUInt16(data, pos + 2);
            var size = BitConverter.ToInt32(data, pos + 4);
            if (size <= 0 || pos + size > data.Length)
                break;

            if (type == ResTablePackageType)
                packages.Add(ParsePackage(data, pos, headerSize, size));

            pos += size;
        }

        return packages.Count > 0;
    }

    private static PackageData ParsePackage(byte[] data, int pos, int headerSize, int size)
    {
        var id = data[pos + 8];
        var end = pos + size;
        var cur = pos + headerSize;
        string[]? types = null;
        string[]? keys = null;
        var typeChunks = new List<TypeChunkData>();

        while (cur + 8 <= end)
        {
            var type = BitConverter.ToUInt16(data, cur);
            var hs = BitConverter.ToUInt16(data, cur + 2);
            var chunkSize = BitConverter.ToInt32(data, cur + 4);
            if (chunkSize <= 0 || cur + chunkSize > end)
                break;

            if (type == ResStringPoolType)
            {
                var pool = ReadStringPool(data, cur);
                if (types is null)
                    types = pool;
                else
                    keys ??= pool;
            }
            else if (type == ResTableTypeType)
            {
                var typeId = data[cur + 8];
                var entryCount = BitConverter.ToInt32(data, cur + 12);
                var entriesStart = BitConverter.ToInt32(data, cur + 16);
                // ResTable_config begins at offset 20: size(4) + mcc/mnc(4) + language(2) + country(2)…
                var language = Encoding.ASCII.GetString(data, cur + 28, 2).Trim('\0');
                var country = Encoding.ASCII.GetString(data, cur + 30, 2).Trim('\0');
                typeChunks.Add(new TypeChunkData(cur, hs, typeId, data[cur + 9], entryCount, entriesStart, language, country));
            }

            cur += chunkSize;
        }

        return new PackageData(id, types ?? [], keys ?? [], typeChunks);
    }

    private static IEnumerable<(int Index, int Offset)> EnumerateEntryOffsets(byte[] data, TypeChunkData chunk)
    {
        var sparse = (chunk.Flags & FlagSparse) != 0;
        var offset16 = (chunk.Flags & FlagOffset16) != 0;
        var tablePos = chunk.ChunkPos + chunk.HeaderSize;

        if (sparse)
        {
            for (var i = 0; i < chunk.EntryCount; i++)
            {
                var idx = BitConverter.ToUInt16(data, tablePos + i * 4);
                var offsetDiv4 = BitConverter.ToUInt16(data, tablePos + i * 4 + 2);
                yield return (idx, offsetDiv4 * 4);
            }

            yield break;
        }

        if (offset16)
        {
            for (var i = 0; i < chunk.EntryCount; i++)
            {
                var offsetDiv4 = BitConverter.ToUInt16(data, tablePos + i * 2);
                if (offsetDiv4 == 0xFFFF)
                    continue;
                yield return (i, offsetDiv4 * 4);
            }

            yield break;
        }

        for (var i = 0; i < chunk.EntryCount; i++)
        {
            var offset = BitConverter.ToInt32(data, tablePos + i * 4);
            if (unchecked((uint)offset) == 0xFFFFFFFFu)
                continue;
            yield return (i, offset);
        }
    }

    private static bool TryReadSimpleValue(byte[] data, int entryPos, out byte dataType, out int payload)
    {
        dataType = 0;
        payload = 0;
        if (entryPos < 0 || entryPos + 8 > data.Length)
            return false;

        var entrySize = BitConverter.ToUInt16(data, entryPos);
        var flags = BitConverter.ToUInt16(data, entryPos + 2);
        if (entrySize < 8 || (flags & EntryFlagComplex) != 0)
            return false;

        var valuePos = entryPos + entrySize;
        if (valuePos + 8 > data.Length)
            return false;

        dataType = data[valuePos + 3];
        payload = BitConverter.ToInt32(data, valuePos + 4);
        return true;
    }

    private static string[] ReadStringPool(byte[] data, int offset)
    {
        var headerSize = BitConverter.ToUInt16(data, offset + 2);
        var stringCount = BitConverter.ToInt32(data, offset + 8);
        var flags = BitConverter.ToInt32(data, offset + 16);
        var stringsStart = BitConverter.ToInt32(data, offset + 20);
        var utf8 = (flags & 0x100) != 0;
        var result = new string[stringCount];
        var baseOff = offset + stringsStart;

        for (var i = 0; i < stringCount; i++)
        {
            var stringOffset = BitConverter.ToInt32(data, offset + headerSize + i * 4);
            var p = baseOff + stringOffset;
            if (utf8)
            {
                p = ReadUtf8Length(data, p, out _);
                p = ReadUtf8Length(data, p, out var byteLen);
                result[i] = Encoding.UTF8.GetString(data, p, Math.Min(byteLen, Math.Max(0, data.Length - p)));
            }
            else
            {
                var charLen = (int)BitConverter.ToUInt16(data, p);
                p += 2;
                if ((charLen & 0x8000) != 0)
                {
                    charLen = ((charLen & 0x7FFF) << 16) | BitConverter.ToUInt16(data, p);
                    p += 2;
                }

                var byteLen = Math.Min(charLen * 2, Math.Max(0, data.Length - p));
                result[i] = Encoding.Unicode.GetString(data, p, byteLen);
            }
        }

        return result;
    }

    private static int ReadUtf8Length(byte[] data, int pos, out int length)
    {
        var b0 = data[pos];
        if ((b0 & 0x80) == 0)
        {
            length = b0;
            return pos + 1;
        }

        length = ((b0 & 0x7F) << 8) | data[pos + 1];
        return pos + 2;
    }

    private sealed record PackageData(int Id, string[] Types, string[] Keys, List<TypeChunkData> TypeChunks);

    private sealed record TypeChunkData(
        int ChunkPos,
        int HeaderSize,
        int TypeId,
        byte Flags,
        int EntryCount,
        int EntriesStart,
        string Language,
        string Country);
}
