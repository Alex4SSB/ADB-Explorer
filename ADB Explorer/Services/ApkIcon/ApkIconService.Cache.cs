using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{

    private static void InvalidatePackageCache(LogicalDeviceViewModel device, string packageName)
    {
        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (!cache.TryGetValue(packageName, out var entry))
                return;

            if (IsSuccessfulIconExt(entry.IconExt))
            {
                var path = GetLocalIconPath(device.SerialNumber, packageName, entry.IconExt);
                try { if (File.Exists(path)) File.Delete(path); } catch { /* ignore */ }
            }

            cache.Remove(packageName);
            WriteCache(device.SerialNumber, cache);
        }
    }


    /// <param name="packageName">Android package id used as the CSV / local-file cache key.</param>
    public static BitmapSource? TryGetCachedIcon(LogicalDeviceViewModel device, string packageName)
        => TryGetCachedIconCore(device, packageName, requireToday: true);


    /// <summary>
    /// Last successful icon on disk, including a previous day's entry. Used to keep the tile
    /// populated while a date-rollover re-check runs.
    /// </summary>
    private static BitmapSource? TryGetStoredIcon(LogicalDeviceViewModel device, string packageName)
        => TryGetCachedIconCore(device, packageName, requireToday: false);


    private static BitmapSource? TryGetCachedIconCore(
        LogicalDeviceViewModel device,
        string packageName,
        bool requireToday)
    {
        EnsureThemeContrastHook();

        if (device is null || string.IsNullOrEmpty(packageName) || !IsEnabled)
            return null;

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (cache.TryGetValue(packageName, out var entry))
            {
                if (requireToday && entry.CheckedDate != DateOnly.FromDateTime(DateTime.Today))
                    return null;

                if (IsSuccessfulIconExt(entry.IconExt))
                {
                    var localPath = GetLocalIconPath(device.SerialNumber, packageName, entry.IconExt);
                    if (File.Exists(localPath))
                        return ForDisplay(DecodeBitmap(localPath));
                }

                // Same-day row whose recorded file is missing (tiny system rasters used to
                // write PNG bytes under the source .webp/.jpg name). Reuse any copy on disk.
                return TryDecodeExistingIconFile(device.SerialNumber, packageName);
            }

            if (requireToday)
                return null;

            return TryDecodeExistingIconFile(device.SerialNumber, packageName);
        }
    }


    private static bool IsIconFreshToday(LogicalDeviceViewModel device, string packageName)
    {
        if (device is null || string.IsNullOrEmpty(packageName))
            return false;

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            return cache.TryGetValue(packageName, out var entry)
                   && entry.CheckedDate == DateOnly.FromDateTime(DateTime.Today)
                   && IsSuccessfulIconExt(entry.IconExt);
        }
    }


    public static string? TryGetCachedLabel(LogicalDeviceViewModel device, string packageName)
    {
        EnsureUiLanguageHook();

        if (device is null || string.IsNullOrEmpty(packageName))
            return null;

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (!cache.TryGetValue(packageName, out var entry))
                return null;

            var picked = PickLocalizedLabel(entry.Label);
            return IsUsableDisplayLabel(picked) ? picked : null;
        }
    }


    private static void ApplyCachedLabel(LogicalDeviceViewModel device, Package package)
    {
        if (string.IsNullOrEmpty(package.Name))
            return;

        var label = TryGetCachedLabel(device, package.Name);
        if (!string.IsNullOrWhiteSpace(label))
            package.Label = label;
    }


    /// <summary>
    /// True when this package already has a settled "no icon" result that must not
    /// trigger another APK unzip: a <see cref="FailMarker"/> (any day — overlays never
    /// grow a launcher icon). Empty <see cref="ApkIconCacheEntry.IconExt"/> means the
    /// label was persisted before icon work finished and must be retried.
    /// </summary>
    private static bool HasSettledIconMiss(LogicalDeviceViewModel device, string packageName)
    {
        if (device is null || string.IsNullOrEmpty(packageName) || IsCalendarPackage(packageName))
            return false;

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            return cache.TryGetValue(packageName, out var entry)
                   && IsSettledIconMiss(entry, packageName);
        }
    }


    private static bool IsSettledIconMiss(in ApkIconCacheEntry entry, string packageName)
    {
        if (IsCalendarPackage(packageName) || IsSuccessfulIconExt(entry.IconExt))
            return false;

        return entry.IconExt == FailMarker;
    }


    private static bool NeedsLabelFetch(LogicalDeviceViewModel device, string packageName)
    {
        EnsureUiLanguageHook();

        lock (GetDeviceLock(device.SerialNumber))
        {
            var cache = GetOrLoadCache(device.SerialNumber);
            if (!cache.TryGetValue(packageName, out var entry))
                return true;

            var locale = GetAppLocaleKey();
            var map = ParseLocalizedLabels(entry.Label);
            if (map.TryGetValue(locale, out var forLocale))
            {
                if (IsUsableDisplayLabel(forLocale))
                    return false;

                // Failed for this locale today — do not retry until the date rolls.
                return forLocale != FailMarker
                       || entry.CheckedDate != DateOnly.FromDateTime(DateTime.Today);
            }

            // Other locales may exist — still need a fetch for the current UI language.
            return true;
        }
    }


    /// <summary>True when a display label is real (not missing, fail marker, pseudo-locale, or lossy junk).</summary>
    private static bool IsUsableDisplayLabel(string? label)
        => !string.IsNullOrWhiteSpace(label)
           && label != FailMarker
           && !ArscResourceResolver.IsPseudoAccentLabel(label)
           && !IsCorruptCachedLabel(label);


    /// <summary>
    /// Detects labels mangled by ANSI/Default round-trips (Hebrew → <c>????</c>) or bad UTF-8 (<c>U+FFFD</c>).
    /// Those must not block re-fetch from the APK.
    /// </summary>
    private static bool IsCorruptCachedLabel(string label)
    {
        if (label.Contains('\uFFFD', StringComparison.Ordinal))
            return true;

        // Multilang field: inspect each value without re-entering parse migration.
        if (label.Contains('=', StringComparison.Ordinal))
        {
            var any = false;
            var allBad = true;
            foreach (var part in SplitLocalizedLabelParts(label))
            {
                var eq = part.IndexOf('=');
                if (eq <= 0)
                    continue;
                var value = UnescapeLocalizedLabelValue(part[(eq + 1)..]);
                if (string.IsNullOrEmpty(value))
                    continue;
                any = true;
                if (!IsCorruptCachedLabel(value) && !ArscResourceResolver.IsPseudoAccentLabel(value))
                    allBad = false;
            }

            return any && allBad;
        }

        var significant = 0;
        var placeholders = 0;
        foreach (var c in label)
        {
            if (char.IsWhiteSpace(c))
                continue;

            significant++;
            if (c == '?')
                placeholders++;
        }

        return significant > 0 && placeholders * 2 >= significant;
    }


    /// <summary>
    /// Stable CSV key for the current app UI language (<c>fr</c>, <c>he</c>, <c>en</c>, …).
    /// Uses <see cref="AppSettings.ActualUICulture"/> so an unset (invariant) preference
    /// resolves to the real OS UI language — never <c>*</c> or invariant <c>iv</c>.
    /// </summary>
    private static string GetAppLocaleKey()
    {
        var culture = Data.Settings.ActualUICulture;
        if (culture.Equals(CultureInfo.InvariantCulture))
            culture = Data.Settings.OriginalUICulture;

        var lang = culture.TwoLetterISOLanguageName;
        if (string.IsNullOrEmpty(lang) || lang.Equals("iv", StringComparison.OrdinalIgnoreCase))
            return "en";

        return lang.ToLowerInvariant();
    }


    private static Dictionary<string, string> ParseLocalizedLabels(string? field)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(field) || field == FailMarker)
            return result;

        field = field.Trim();
        var localeKey = GetAppLocaleKey();

        // Legacy bare label (no lang=) → attribute to the actual UI locale.
        if (!field.Contains('=', StringComparison.Ordinal))
        {
            if (IsUsableDisplayLabel(field))
                result[localeKey] = field;
            return result;
        }

        foreach (var part in SplitLocalizedLabelParts(field))
        {
            var eq = part.IndexOf('=');
            if (eq <= 0)
                continue;

            var key = part[..eq].Trim();
            var value = UnescapeLocalizedLabelValue(part[(eq + 1)..]);
            if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value))
                continue;

            // Drop Android pseudo-accent values so the locale is fetched again.
            if (ArscResourceResolver.IsPseudoAccentLabel(value))
                continue;

            result[key] = value;
        }

        // Migrate obsolete "*" keys (from an earlier invariant/default encoding) onto the real locale.
        if (result.Remove("*", out var starVal))
        {
            if (IsUsableDisplayLabel(starVal)
                && (!result.TryGetValue(localeKey, out var existing)
                    || !IsUsableDisplayLabel(existing)
                    || ArscResourceResolver.IsPseudoAccentLabel(existing)))
            {
                result[localeKey] = starVal;
            }
        }

        return result;
    }


    private static IEnumerable<string> SplitLocalizedLabelParts(string field)
    {
        var start = 0;
        for (var i = 0; i < field.Length; i++)
        {
            if (field[i] == '\\' && i + 1 < field.Length)
            {
                i++;
                continue;
            }

            if (field[i] == ';')
            {
                if (i > start)
                    yield return field[start..i];
                start = i + 1;
            }
        }

        if (start < field.Length)
            yield return field[start..];
    }


    private static string EscapeLocalizedLabelValue(string value)
        => value.Replace("\\", "\\\\", StringComparison.Ordinal)
                .Replace(";", "\\;", StringComparison.Ordinal)
                .Replace("=", "\\=", StringComparison.Ordinal)
                .Replace("|", "\\|", StringComparison.Ordinal);


    private static string UnescapeLocalizedLabelValue(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\\' && i + 1 < value.Length)
            {
                sb.Append(value[i + 1]);
                i++;
                continue;
            }

            sb.Append(value[i]);
        }

        return sb.ToString();
    }


    private static string EncodeLocalizedLabels(IReadOnlyDictionary<string, string> map)
    {
        if (map.Count == 0)
            return "";

        return string.Join(';', map
            .OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
            .Select(kv => $"{kv.Key}={EscapeLocalizedLabelValue(kv.Value)}"));
    }


    /// <summary>Merges <paramref name="localeValue"/> for the current UI locale into an existing label field.</summary>
    private static string MergeLocaleLabel(string? existingField, string localeValue)
    {
        var map = ParseLocalizedLabels(existingField == FailMarker ? null : existingField);
        map[GetAppLocaleKey()] = localeValue;
        return EncodeLocalizedLabels(map);
    }


    private static string? PickLocalizedLabel(string? field)
    {
        if (string.IsNullOrWhiteSpace(field) || field == FailMarker)
            return null;

        var map = ParseLocalizedLabels(field);
        if (map.Count == 0)
            return null;

        var locale = GetAppLocaleKey();
        if (map.TryGetValue(locale, out var exact) && IsUsableDisplayLabel(exact))
            return exact;

        // Android legacy aliases (stored under he while resources used iw — we store app keys only).
        foreach (var tag in ArscResourceResolver.AndroidLanguageTagsFor(Data.Settings.ActualUICulture))
        {
            var lang = tag.Contains('-', StringComparison.Ordinal) ? tag.Split('-')[0] : tag;
            // Map android iw → app he for lookup if somehow stored that way.
            var appKey = lang switch
            {
                "iw" => "he",
                "in" => "id",
                "ji" => "yi",
                _ => lang,
            };
            if (map.TryGetValue(appKey, out var aliased) && IsUsableDisplayLabel(aliased))
                return aliased;
        }

        if (map.TryGetValue("en", out var english) && IsUsableDisplayLabel(english))
            return english;

        return map.Values.FirstOrDefault(IsUsableDisplayLabel);
    }


    private static void EnsureUiLanguageHook()
    {
        if (_uiLanguageHooked)
            return;

        _uiLanguageHooked = true;
        Data.Settings.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is not (nameof(AppSettings.UILanguage) or nameof(AppSettings.UICulture)))
                return;

            App.SafeBeginInvoke(OnUiLanguageChanged);
        };
    }


    private static void OnUiLanguageChanged()
    {
        if (Data.Packages is null || Data.Packages.Count == 0)
            return;

        if (Data.DevicesObject?.Current is not { } device)
            return;

        foreach (var package in Data.Packages)
        {
            ApplyCachedLabel(device, package);
            BeginEnsureLabelForPackage(package, ApkLoadPriority.Background);
        }
    }


    private static bool IsSuccessfulIconExt(string? iconExt)
        => !string.IsNullOrEmpty(iconExt)
           && iconExt != FailMarker
           && iconExt.StartsWith('.');


    private static string SanitizePackageFileName(string packageName)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = packageName.Trim().ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (invalid.Contains(chars[i]) || chars[i] is '|' or '/')
                chars[i] = '_';
        }

        var name = new string(chars);
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }


    private static string NormalizeCrc(string crc)
        => crc.Trim().ToUpperInvariant();


    private static string GetLocalIconDirectory(string serialNumber)
        => Path.Combine(Data.AppDataPath, serialNumber, ICONS_SUBFOLDER);


    private static string GetLocalIconPath(string serialNumber, string packageName, string iconExt)
    {
        var fileName = SanitizePackageFileName(packageName);
        if (IsDeskclockPackage(packageName) || IsCalendarPackage(packageName))
            fileName += DynamicLauncherIconTag;
        return Path.Combine(GetLocalIconDirectory(serialNumber), fileName + iconExt);
    }


    private static BitmapSource? TryDecodeExistingIconFile(string serialNumber, string packageName)
    {
        foreach (var path in EnumerateLocalIconCandidatePaths(serialNumber, packageName))
        {
            if (!File.Exists(path))
                continue;

            var decoded = DecodeBitmap(path);
            if (decoded is not null)
                return ForDisplay(decoded);
        }

        return null;
    }


    private static IEnumerable<string> EnumerateLocalIconCandidatePaths(string serialNumber, string packageName)
    {
        var dir = GetLocalIconDirectory(serialNumber);
        var baseName = SanitizePackageFileName(packageName);
        var tagged = IsDeskclockPackage(packageName) || IsCalendarPackage(packageName);
        string[] names = tagged
            ? [baseName + DynamicLauncherIconTag, baseName]
            : [baseName];
        string[] exts = [".png", ".webp"];
        foreach (var name in names)
        {
            foreach (var ext in exts)
                yield return Path.Combine(dir, name + ext);
        }
    }


    private static object GetDeviceLock(string serialNumber)
        => DeviceLocks.GetOrAdd(serialNumber, _ => new object());


    private static Dictionary<string, ApkIconCacheEntry> GetOrLoadCache(string serialNumber)
    {
        if (DeviceCaches.TryGetValue(serialNumber, out var cached))
            return cached;

        var loaded = ReadCache(serialNumber);
        DeviceCaches[serialNumber] = loaded;
        return loaded;
    }


    private static Dictionary<string, ApkIconCacheEntry> ReadCache(string serialNumber)
    {
        var result = new Dictionary<string, ApkIconCacheEntry>(StringComparer.Ordinal);
        var csvPath = Path.Combine(Data.AppDataPath, serialNumber, CSV_FILE);
        if (!File.Exists(csvPath))
            return result;

        string[] lines;
        try
        {
            lines = File.ReadAllLines(csvPath, CsvEncoding);
        }
        catch
        {
            return result;
        }

        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            if (!TryParseCacheLine(line, out var packageName, out var crc, out var date, out var iconExt, out var label, out var clockHands))
                continue;

            result[packageName] = new ApkIconCacheEntry(crc, date, iconExt, label, clockHands);
        }

        return result;
    }


    /// <summary>
    /// <c>package|crc|yyyy-MM-dd|ext|label[|baked|overlay]</c>.
    /// Label may contain <c>|</c> (escaped as <c>\|</c>, or legacy unescaped);
    /// the last field is clock hands only when it is <c>baked</c> or <c>overlay</c>.
    /// </summary>
    private static bool TryParseCacheLine(
        string line,
        out string packageName,
        out string crc,
        out DateOnly date,
        out string iconExt,
        out string? label,
        out string? clockHands)
    {
        packageName = "";
        crc = "";
        date = default;
        iconExt = "";
        label = null;
        clockHands = null;

        var parts = line.Split('|');
        if (parts.Length < 4)
            return false;

        packageName = parts[0];
        if (string.IsNullOrEmpty(packageName))
            return false;

        crc = NormalizeCrc(parts[1]);
        if (!DateOnly.TryParseExact(parts[2], CsvDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out date))
            return false;

        iconExt = NormalizeIconExtField(parts[3]);
        if (parts.Length == 4)
            return true;

        var lastHands = NormalizeClockHandsField(parts[^1]);
        if (parts.Length >= 6 && lastHands is not null)
        {
            clockHands = lastHands;
            label = NormalizeLabelField(string.Join('|', parts[4..^1]));
        }
        else
        {
            label = NormalizeLabelField(string.Join('|', parts[4..]));
        }

        return true;
    }


    /// <summary>
    /// Accepts extension-only values (<c>.webp</c>), fail marker, or legacy full filenames (<c>pkg.webp</c>).
    /// </summary>
    private static string NormalizeIconExtField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return "";

        field = field.Trim();
        if (field == FailMarker)
            return FailMarker;

        if (field.StartsWith('.'))
            return field.ToLowerInvariant();

        var ext = Path.GetExtension(field);
        return string.IsNullOrEmpty(ext) ? "" : ext.ToLowerInvariant();
    }


    private static string? NormalizeLabelField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        field = field.Trim();
        if (field == FailMarker)
            return FailMarker;

        // Re-encode so legacy bare / "*" keys become the real UI locale and pseudo-accent
        // values are dropped (those locales will be re-fetched).
        var map = ParseLocalizedLabels(field);
        if (map.Count == 0)
            return null;

        return EncodeLocalizedLabels(map);
    }


    private static void WriteCache(string serialNumber, Dictionary<string, ApkIconCacheEntry> cache)
    {
        var deviceDir = Path.Combine(Data.AppDataPath, serialNumber);
        Directory.CreateDirectory(deviceDir);
        var csvPath = Path.Combine(deviceDir, CSV_FILE);
        var lines = cache.Select(kvp =>
        {
            var iconExt = string.IsNullOrEmpty(kvp.Value.IconExt) ? "" : kvp.Value.IconExt;
            var label = string.IsNullOrEmpty(kvp.Value.Label) ? "" : kvp.Value.Label;
            var line = $"{kvp.Key}|{kvp.Value.ManifestCrc}|{kvp.Value.CheckedDate.ToString(CsvDateFormat, CultureInfo.InvariantCulture)}|{iconExt}|{label}";
            if (!string.IsNullOrEmpty(kvp.Value.ClockHands))
                line += "|" + kvp.Value.ClockHands;
            return line;
        });
        File.WriteAllText(csvPath, string.Join(Environment.NewLine, lines), CsvEncoding);
    }


    private static BitmapSource? DecodeBitmap(string localPath)
    {
        try
        {
            // WIC's WebP decoder drops alpha (VP8L → Bgr32 / opaque black).
            // Also sniff content: extensionless WebP was historically cached as .png.
            var isWebp = Path.GetExtension(localPath).Equals(".webp", StringComparison.OrdinalIgnoreCase)
                || FileLooksLikeWebp(localPath);
            if (isWebp)
                return DecodeWebpWithAlpha(localPath) ?? DecodeBitmapWithWic(localPath);

            return DecodeBitmapWithWic(localPath);
        }
        catch
        {
            return null;
        }
    }


    private static BitmapSource? DecodeBitmapWithWic(string localPath)
    {
        using var stream = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.StreamSource = stream;
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.EndInit();
        bitmap.Freeze();
        return bitmap;
    }


    private static BitmapSource? DecodeWebpWithAlpha(string localPath)
    {
        using var skBitmap = SKBitmap.Decode(localPath);
        if (skBitmap is null || skBitmap.Width <= 0 || skBitmap.Height <= 0)
            return null;

        using var bgra = skBitmap.ColorType == SKColorType.Bgra8888 && skBitmap.AlphaType != SKAlphaType.Opaque
            ? null
            : skBitmap.Copy(SKColorType.Bgra8888);

        var source = bgra ?? skBitmap;
        var stride = source.RowBytes;
        var height = source.Height;
        var width = source.Width;
        var buffer = new byte[stride * height];
        Marshal.Copy(source.GetPixels(), buffer, 0, buffer.Length);

        var writeable = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgra32, null);
        writeable.WritePixels(new Int32Rect(0, 0, width, height), buffer, stride, 0);
        writeable.Freeze();
        return writeable;
    }


    private static bool FileLooksLikeWebp(string localPath)
    {
        try
        {
            using var fs = new FileStream(localPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> hdr = stackalloc byte[12];
            var read = fs.Read(hdr);
            return DetectRasterExtension(hdr[..read]) == ".webp";
        }
        catch
        {
            return false;
        }
    }


    private static string? TryReadPackageName(byte[] manifestBytes, byte[] resourcesBytes)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            var arsc = new ArscFile(resourcesBytes);
            var manifest = AndroidManifest.Load(axml, arsc);
            if (!string.IsNullOrWhiteSpace(manifest?.Package))
                return manifest.Package.Trim();

            return axml.RootNode is null ? null : GetAttributeValue(axml.RootNode, "package");
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Filename tag so clock (face-only) and calendar (day-of-month) caches are not reused
    /// from earlier builds that baked hands or a stale date into the PNG.
    /// </summary>
    private const string DynamicLauncherIconTag = ".dyn";
}
