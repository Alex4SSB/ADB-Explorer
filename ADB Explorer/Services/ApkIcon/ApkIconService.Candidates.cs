using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
    private static async Task<List<string>> ResolveIconCandidatesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        byte[] manifestBytes,
        byte[] resourcesBytes,
        CancellationToken cancellationToken
        , ApkLoadTiming? timing = null
        )
    {
        try
        {
            var arsc = new ArscFile(resourcesBytes);
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));

            // Prefer walking AXML attributes — ApkApplication.Icon throws on "@7F…" refs.
            var iconRef = AxmlManifestReader.TryGetApplicationAttribute(manifestBytes, AxmlManifestReader.AttrIcon)
                ?? FindApplicationAttribute(axml.RootNode, "icon")
                ?? TryGetTypedApplicationIcon(axml, arsc);

            if (string.IsNullOrEmpty(iconRef))
            {
                Mark(timing, "ResolveIconCandidates: no icon ref → string-pool");
                return FindLikelyIconPathsInStringPool(arsc);
            }

            Mark(timing, $"ResolveIconCandidates: iconRef={iconRef}");

            // Resolve the manifest icon id only — do not fall back to string-pool or brand
            // guesses here. Density-split APKs often own the real adaptive wrapper while the
            // base arsc lists the id as INVALID. Broad key-hint fallbacks previously
            // matched chrome glyphs and returned notification dots before splits ran.
            var paths = ResolveIconRefToPathsStrict(iconRef, arsc, resourcesBytes);
            if (paths.Count == 0)
            {
                Mark(timing, "ResolveIconCandidates: strict resolve empty");
                return [];
            }

            Mark(timing, $"ResolveIconCandidates: {paths.Count} strict paths");

            // Adaptive wrappers (anydpi / *launcher* XML) before density rasters.
            var adaptivePreferred = paths
                .Where(IsAdaptiveWrapperPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var xmlMember in adaptivePreferred)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mark(timing, $"probe adaptive wrapper: {xmlMember}");
                var xmlBytes = await ProbeApkMemberBytesAsync(device, apkPath, xmlMember, cancellationToken)
                    .ConfigureAwait(false);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                {
                    Mark(timing, $"adaptive wrapper confirmed: {xmlMember}");
                    // Always compose the adaptive wrapper. Layer names like
                    // ic_launcher_background can still hold real product art
                    // (custom plates); same-named mipmap rasters are often social badges.
                    return [xmlMember];
                }
            }

            // Prefer pre-rendered density rasters over distorting vectors.
            // Skip bare *_background layers (themed alternate plates).
            var rasters = RankIconCandidates(paths.Where(p =>
                !p.Contains("_background.", StringComparison.OrdinalIgnoreCase)
                && !p.Contains("_background_", StringComparison.OrdinalIgnoreCase)));
            if (rasters.Count > 0)
            {
                Mark(timing, $"ResolveIconCandidates: {rasters.Count} ranked rasters");
                return rasters;
            }

            // Any remaining XML from the icon ref (including obfuscated names like res/qq.xml).
            var xmlMembers = paths
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var xmlMember in xmlMembers)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mark(timing, $"probe xml member: {xmlMember}");
                var xmlBytes = await ProbeApkMemberBytesAsync(device, apkPath, xmlMember, cancellationToken)
                    .ConfigureAwait(false);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                // Keep the adaptive wrapper — never return bare foreground vectors (white-on-transparent).
                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                {
                    Mark(timing, $"xml adaptive confirmed: {xmlMember}");
                    return [xmlMember];
                }

                if (ApkVectorIconRenderer.IsVectorDrawable(xmlBytes))
                {
                    Mark(timing, $"xml vector confirmed: {xmlMember}");
                    return [xmlMember];
                }
            }

            var images = paths.Where(IsImagePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (images.Count > 0)
                return RankIconCandidates(images);

            // String-pool adaptive wrappers as last resort.
            var poolAdaptive = FindLikelyIconPathsInStringPool(arsc)
                .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (poolAdaptive.Count > 0)
                return poolAdaptive;
        }
        catch (Exception e)
        {
#if !DEPLOY
            DebugLog.PrintLine($"APK icon resolve failed for {apkPath}: {e.Message}");
#endif
        }

        return [];
    }

    private static string? TryGetTypedApplicationIcon(AxmlFile axml, ArscFile arsc)
    {
        try
        {
            var manifest = AndroidManifest.Load(axml, arsc);
            return manifest?.Application?.Node is { } node
                ? GetAttributeValue(node, "icon")
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Resolves <paramref name="iconRef"/> to archive members without string-pool fallbacks.
    /// Empty means the id is missing from this <c>resources.arsc</c> (often a density split owns it).
    /// </summary>
    private static List<string> ResolveIconRefToPathsStrict(string iconRef, ArscFile arsc, byte[] resourcesBytes)
    {
        iconRef = ArchivePath.NormalizeInternal(iconRef.Trim());
        if (IsImagePath(iconRef) || iconRef.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return [iconRef];

        if (!TryParseResourceId(iconRef, out var resourceId))
            return [];

        var nativePaths = PreferIconPaths(ArscResourceResolver.ResolvePaths(resourcesBytes, resourceId));
        if (nativePaths.Count > 0)
            return nativePaths;

        return PreferIconPaths(GetResourcePaths(arsc, resourceId));
    }

    /// <summary>
    /// All file paths for an icon resource id — keeps density PNGs alongside adaptive XML
    /// (<c>mipmap/launcher_icon</c> may have broken adaptive layers but valid density PNGs).
    /// </summary>
    private static List<string> ResolveIconRefToAllPaths(string iconRef, ArscFile arsc, byte[] resourcesBytes)
    {
        iconRef = ArchivePath.NormalizeInternal(iconRef.Trim());
        if (IsImagePath(iconRef) || iconRef.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return [iconRef];

        if (!TryParseResourceId(iconRef, out var resourceId))
            return [];

        return ArscResourceResolver.ResolvePaths(resourcesBytes, resourceId)
            .Concat(GetResourcePaths(arsc, resourceId))
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsAdaptiveWrapperPath(string path)
    {
        if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;

        if (path.Contains("anydpi", StringComparison.OrdinalIgnoreCase)
            || path.Contains("adaptive", StringComparison.OrdinalIgnoreCase)
            || path.Contains("ic_launcher", StringComparison.OrdinalIgnoreCase))
            return true;

        // Named wrappers like drawable-anydpi-v26/zm_launcher.xml are covered above via anydpi.
        // Also accept non-anydpi *launcher*.xml that are not adaptive layers / splash / logos.
        if (!path.Contains("launcher", StringComparison.OrdinalIgnoreCase))
            return false;

        ReadOnlySpan<string> layerSuffixes =
        [
            "_foreground", "_background", "_splash", "_logo", "_banner", "_round_foreground",
        ];
        foreach (var suffix in layerSuffixes)
        {
            if (path.Contains(suffix, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    /// <summary>
    /// AlphaOmega ResourceMap often lists themed <c>*_background</c> rasters alongside the real
    /// adaptive XML for the same id (themed alternate plates). Prefer the adaptive wrapper.
    /// </summary>
    private static List<string> PreferIconPaths(IEnumerable<string> paths)
    {
        var list = paths
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(IsNightQualifiedPath) // light / default before night
            .ToList();

        if (list.Count <= 1)
            return list;

        var adaptiveXml = list
            .Where(IsAdaptiveWrapperPath)
            .OrderBy(IsNightQualifiedPath)
            .ThenBy(p => p.Contains("default", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Contains("anydpi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => p.Length)
            .ToList();

        if (adaptiveXml.Count > 0)
            return adaptiveXml;

        // No XML — drop bare adaptive-layer backgrounds if a non-background raster exists.
        var withoutBg = list
            .Where(p => !p.Contains("_background.", StringComparison.OrdinalIgnoreCase)
                        && !p.Contains("_background_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        return withoutBg.Count > 0 ? withoutBg : list;
    }

    /// <summary>
    /// Keep one path per basename — the highest-density folder (xxxhdpi ≻ … ≻ mdpi).
    /// </summary>
    private static List<string> PreferHighestDensityOnly(IEnumerable<string> paths)
    {
        return paths
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrEmpty(p))
            .GroupBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderBy(IsNightQualifiedPath)
                .ThenByDescending(DensityRank)
                .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
                .First())
            .OrderBy(IsNightQualifiedPath)
            .ThenByDescending(DensityRank)
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<string> FindLikelyIconPathsInStringPool(ArscFile arsc)
    {
        var strings = arsc.ValueStringPool?.Strings;
        if (strings is null || strings.Length == 0)
            return [];

        return strings
            .Where(s => !string.IsNullOrEmpty(s)
                        && s.StartsWith("res/", StringComparison.OrdinalIgnoreCase)
                        && (IsImagePath(s) || s.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        && IsLikelyLauncherPath(s))
            .Select(ArchivePath.NormalizeInternal)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Prefer adaptive wrappers (for bg+fg compositing), then dense rasters.
            .OrderBy(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase) && p.Contains("anydpi", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(p => IsImagePath(p) ? 0 : 1)
            .ThenByDescending(DensityRank)
            .ToList();
    }

    private static bool IsLikelyLauncherPath(string path)
    {
        // Stock AS layer XML halves are incomplete alone (green plate or transparent Bugdroid).
        // Prefer compositing / DefaultAndroidPackageIcon over either half as a final icon.
        if (IsStockAndroidStudioLayerPath(path))
            return false;

        return path.Contains("ic_foreground", StringComparison.OrdinalIgnoreCase)
               || path.Contains("icon_launcher", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/ic_launcher.", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/ic_launcher_round.", StringComparison.OrdinalIgnoreCase)
               || path.Contains("/icon.", StringComparison.OrdinalIgnoreCase)
               || path.Contains("launcher", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Leftover Android Studio <c>ic_launcher_background</c> / <c>ic_launcher_foreground</c> XML
    /// (not density PNGs that happen to share the name).
    /// </summary>
    private static bool IsStockAndroidStudioLayerPath(string path)
    {
        if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;

        var name = Path.GetFileNameWithoutExtension(path);
        return name.Equals("ic_launcher_background", StringComparison.OrdinalIgnoreCase)
               || name.Equals("ic_launcher_foreground", StringComparison.OrdinalIgnoreCase);
    }

    private static List<string> GetResourcePaths(ArscFile arsc, int resourceId)
    {
        if (!arsc.ResourceMap.TryGetValue(resourceId, out var rows) || rows is null || rows.Count == 0)
            return [];

        return rows
            .Select(row => row.Value)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => ArchivePath.NormalizeInternal(value.Trim()))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Drawable/mipmap file paths for a resource id. Native arsc first — AlphaOmega's
    /// <see cref="ArscFile.ResourceMap"/> frequently maps the wrong pool string.
    /// </summary>
    private static List<string> ResolveDrawableFilePaths(ArscFile arsc, byte[] resourcesBytes, int resourceId)
    {
        var native = ArscResourceResolver.ResolvePaths(resourcesBytes, resourceId)
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        IEnumerable<string> candidates = native.Count > 0 ? native : GetResourcePaths(arsc, resourceId);

        return candidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p) && !IsColorResourcePath(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool IsColorResourcePath(string path)
        => path.Contains("/color/", StringComparison.OrdinalIgnoreCase)
           || path.Contains(@"\color\", StringComparison.OrdinalIgnoreCase)
           || path.StartsWith("res/color", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseResourceId(string value, out int resourceId)
    {
        resourceId = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.StartsWith('@'))
            value = value[1..];

        // Named refs like @mipmap/ic_launcher are not resolved here.
        if (value.Contains('/'))
            return false;

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            return int.TryParse(value.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resourceId);

        return int.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resourceId);
    }

    private static bool IsImagePath(string path)
    {
        var ext = Path.GetExtension(path);
        return ImageExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Obfuscated APK members (WebView <c>res/9M</c>) omit extensions; sniff container magic.
    /// </summary>
    private static string? DetectRasterExtension(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 12
            && bytes[0] == (byte)'R'
            && bytes[1] == (byte)'I'
            && bytes[2] == (byte)'F'
            && bytes[3] == (byte)'F'
            && bytes[8] == (byte)'W'
            && bytes[9] == (byte)'E'
            && bytes[10] == (byte)'B'
            && bytes[11] == (byte)'P')
        {
            return ".webp";
        }

        if (bytes.Length >= 8
            && bytes[0] == 0x89
            && bytes[1] == (byte)'P'
            && bytes[2] == (byte)'N'
            && bytes[3] == (byte)'G'
            && bytes[4] == 0x0D
            && bytes[5] == 0x0A
            && bytes[6] == 0x1A
            && bytes[7] == 0x0A)
        {
            return ".png";
        }

        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return ".jpg";
        }

        return null;
    }

    /// <summary>
    /// Some packs store PNG/WebP without an extension (<c>res/raw/…</c> or root entries).
    /// </summary>
    private static bool IsExtensionlessRasterCandidate(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || path.Contains(' ', StringComparison.Ordinal))
            return false;
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.IsNullOrEmpty(Path.GetExtension(path)))
            return false;

        var name = Path.GetFileName(path);
        return name.Length is >= 1 and <= 64
               && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-');
    }

    /// <summary>
    /// Last-resort discovery when manifest/arsc resolution finds nothing.
    /// </summary>
    private static List<ArchiveEntry> DiscoverIconMembersOnDevice(
        string deviceId,
        string apkPath,
        CancellationToken cancellationToken)
    {
        var unzip = ShellCommands.TranslateCommand("unzip");
        var apkEsc = AdbService.EscapeAdbShellString(apkPath);
        var script =
            $"{unzip} -l {apkEsc} 2>/dev/null | grep -Ei 'res/(mipmap|drawable)[^/]*/[^/]*(launcher|app_icon|ic_launcher)[^/]*\\.(png|webp|xml)$' | head -n 60";

        _ = AdbService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var stdout,
            out _,
            cancellationToken,
            "-c",
            AdbService.EscapeAdbShellString(script));

        var result = new List<ArchiveEntry>();
        foreach (var rawLine in stdout.Split(AdbService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var match = UnzipListEntryLine().Match(rawLine);
            if (!match.Success)
                continue;

            var name = ArchivePath.NormalizeInternal(match.Groups["Name"].Value.TrimEnd());
            if (string.IsNullOrEmpty(name))
                continue;

            long.TryParse(match.Groups["Length"].Value, out var size);
            result.Add(new ArchiveEntry(name, IsDirectory: false, size, Modified: null));
        }

        return result;
    }

    private static List<string> HeuristicIconCandidates()
    {
        string[] names = ["ic_launcher", "ic_launcher_round", "ic_launcher_foreground", "icon_launcher", "icon", "launcher_icon"];
        // Drawable densities first — some apps ship launcher PNGs only under drawable-*.
        string[] folders =
        [
            "drawable-xxxhdpi-v4", "drawable-xxxhdpi", "drawable-xxhdpi-v4", "drawable-xxhdpi",
            "drawable-xhdpi-v4", "drawable-xhdpi",
            "mipmap-xxxhdpi-v4", "mipmap-xxxhdpi", "mipmap-xxhdpi-v4", "mipmap-xxhdpi",
            "mipmap-xhdpi-v4", "mipmap-xhdpi", "mipmap-hdpi-v4", "mipmap-hdpi",
        ];
        string[] extensions = [".png", ".webp"];

        var result = new List<string>();
        foreach (var folder in folders)
        {
            foreach (var name in names)
            {
                foreach (var ext in extensions)
                    result.Add($"res/{folder}/{name}{ext}");
            }
        }

        // Flutter apps often keep launcher art under assets/.
        result.Add("assets/flutter_assets/images/ic_launcher.png");
        result.Add("assets/flutter_assets/images/ic_launcher.webp");
        result.Add("assets/flutter_assets/AppIcon.png");

        return RankIconCandidates(result);
    }

    /// <summary>
    /// Heuristic paths for probing: top densities per basename so xxhdpi-only packs are not
    /// skipped when xxxhdpi variants dominate a flat density-sorted <c>Take(N)</c>.
    /// </summary>
    private static List<string> HeuristicIconProbeCandidates(int maxPaths)
    {
        const int densitiesPerName = 4;
        // Keep per-name density picks in group order — do not re-sort by density before Take,
        // or only xxxhdpi paths survive.
        return HeuristicIconCandidates()
            .GroupBy(static p => Path.GetFileName(p) ?? p, StringComparer.OrdinalIgnoreCase)
            .SelectMany(static g => g.OrderByDescending(DensityRank).Take(densitiesPerName))
            .Take(maxPaths)
            .ToList();
    }

    private static List<string> RankIconCandidates(IEnumerable<string> candidates)
        => candidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => IsImagePath(p) || IsExtensionlessRasterCandidate(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(IsNightQualifiedPath)
            .ThenByDescending(DensityRank)
            .ThenByDescending(p => IsImagePath(p) ? 1 : 0)
            .ToList();

    /// <summary>
    /// Discovery ranking that keeps adaptive / launcher XML ahead of logos and density rasters.
    /// </summary>
    private static List<string> RankDiscoveredIconCandidates(IEnumerable<string> candidates)
        => candidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(p => !IsStockAndroidStudioLayerPath(p)
                        && (IsImagePath(p)
                            || IsExtensionlessRasterCandidate(p)
                            || p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(IconCandidateScore)
            .ThenBy(IsNightQualifiedPath)
            .ThenByDescending(DensityRank)
            .ToList();

    private static int IconCandidateScore(string path)
    {
        if (IsAdaptiveWrapperPath(path))
            return 4;
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)
            && path.Contains("anydpi", StringComparison.OrdinalIgnoreCase))
            return 3;
        if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            return 1;
        // Prefer full launcher rasters over *_logo / activity glyphs when XML is absent.
        if (path.Contains("_logo", StringComparison.OrdinalIgnoreCase)
            || path.Contains("_splash", StringComparison.OrdinalIgnoreCase))
            return 0;
        return 2;
    }

    private static string? PickBestIconMember(
        IReadOnlyList<string> rankedCandidates,
        IEnumerable<string> availableMembers,
        Func<string, long>? sizeOf = null)
    {
        var present = new HashSet<string>(
            availableMembers.Select(ArchivePath.NormalizeInternal),
            StringComparer.OrdinalIgnoreCase);
        if (rankedCandidates.Count == 0 || present.Count == 0)
            return null;

        // Obfuscated packs store density variants as short res/ names with no
        // mipmap-*dpi* folder — DensityRank ties at 0; prefer the largest pulled bytes.
        return rankedCandidates
            .Select(ArchivePath.NormalizeInternal)
            .Where(present.Contains)
            .OrderByDescending(IconCandidateScore)
            .ThenByDescending(DensityRank)
            // Prefer brand / resolved names over leftover Android Studio templates when tied.
            .ThenBy(StockLauncherTemplatePenalty)
            .ThenByDescending(p => sizeOf?.Invoke(p) ?? 0)
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string? PickBestIconMember(IReadOnlyList<string> rankedCandidates, IReadOnlyList<ArchiveEntry> listing)
    {
        if (rankedCandidates.Count == 0 || listing.Count == 0)
            return null;

        return PickBestIconMember(
            rankedCandidates,
            listing.Where(static e => !e.IsDirectory).Select(static e => e.Path),
            p => FindEntry(listing, p)?.Size ?? 0);
    }

    /// <summary>0 = keep; 1 = demote stock <c>ic_launcher</c> / <c>ic_launcher_round</c> templates.</summary>
    private static int StockLauncherTemplatePenalty(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        if (name.Equals("ic_launcher", StringComparison.OrdinalIgnoreCase)
            || name.Equals("ic_launcher_round", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 0;
    }

    private static int DensityRank(string path)
    {
        for (var i = 0; i < DensityOrder.Length; i++)
        {
            if (path.Contains(DensityOrder[i], StringComparison.OrdinalIgnoreCase))
                return DensityOrder.Length - i;
        }

        return 0;
    }

    /// <summary>0 = default/light; 1 = night-qualified (drawable-night, -night-*, etc.).</summary>
    private static int IsNightQualifiedPath(string path)
        => path.Contains("-night", StringComparison.OrdinalIgnoreCase)
           || path.Contains("/night/", StringComparison.OrdinalIgnoreCase)
            ? 1
            : 0;

    private static ArchiveEntry? FindEntry(IReadOnlyList<ArchiveEntry> entries, string memberName)
    {
        var normalized = ArchivePath.NormalizeInternal(memberName);
        foreach (var entry in entries)
        {
            if (entry.IsDirectory)
                continue;

            if (string.Equals(ArchivePath.NormalizeInternal(entry.Path), normalized, StringComparison.OrdinalIgnoreCase))
                return entry;
        }

        return null;
    }
}
