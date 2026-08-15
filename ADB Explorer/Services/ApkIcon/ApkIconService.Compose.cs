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

    /// <summary>
    /// Order APKs for icon/resource reads: density splits, then <c>base.apk</c>, then
    /// other resource-ish configs. ABI / language / feature modules are last.
    /// </summary>
    private static List<string> PreferApksForRead(IReadOnlyList<string> apkFiles, string baseApk)
    {
        return apkFiles
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(IconApkRank)
            .ThenBy(p => string.Equals(p, baseApk, StringComparison.Ordinal) ? 0 : 1)
            .ThenBy(p => p, StringComparer.Ordinal)
            .ToList();
    }


    /// <summary>
    /// Density splits + base only (base first). Feature / ABI / language modules are excluded.
    /// </summary>
    private static List<string> PreferApksForIconMember(IReadOnlyList<string> apkFiles, string? baseApk = null)
    {
        baseApk ??= apkFiles.FirstOrDefault(static p =>
            Path.GetFileName(p).Equals("base.apk", StringComparison.OrdinalIgnoreCase));

        var preferred = PreferApksForRead(apkFiles, baseApk ?? apkFiles[0])
            .Where(static p => IconApkRank(p) >= 5)
            .ToList();

        if (preferred.Count == 0)
            preferred = PreferApksForRead(apkFiles, baseApk ?? apkFiles[0]);

        // Base first — adaptive XML / vectors almost always live there.
        return
        [
            .. preferred.Where(p => Path.GetFileName(p).Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                || string.Equals(p, baseApk, StringComparison.Ordinal)),
            .. preferred.Where(p => !Path.GetFileName(p).Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(p, baseApk, StringComparison.Ordinal)),
        ];
    }


    private static int IconApkRank(string apkPath)
    {
        var name = Path.GetFileName(apkPath).ToLowerInvariant();

        if (name.Contains("xxxhdpi", StringComparison.Ordinal)) return 50;
        if (name.Contains("xxhdpi", StringComparison.Ordinal)) return 40;
        if (name.Contains("xhdpi", StringComparison.Ordinal)) return 30;
        if (name.Contains("tvdpi", StringComparison.Ordinal)) return 18;
        if (name.Contains("hdpi", StringComparison.Ordinal)) return 20;
        if (name.Contains("mdpi", StringComparison.Ordinal)) return 10;
        if (name.Contains("ldpi", StringComparison.Ordinal)) return 5;

        if (name.Equals("base.apk", StringComparison.OrdinalIgnoreCase)
            || (!name.Contains("split", StringComparison.Ordinal)
                && name.EndsWith(".apk", StringComparison.Ordinal)))
            return IconApkRankBase;

        if (name.Contains("arm64", StringComparison.Ordinal)
            || name.Contains("armeabi", StringComparison.Ordinal)
            || name.Contains("x86_64", StringComparison.Ordinal)
            || name.Contains("x86", StringComparison.Ordinal))
            return -40;

        if (IsLanguageConfigSplitName(name))
            return -30;

        // Feature modules (split_OCRCoreDF.apk, split_FASOpenCVDF.apk, …) — never launcher icons.
        if (name.StartsWith("split_", StringComparison.Ordinal))
            return -20;

        if (name.Contains("config.", StringComparison.Ordinal))
            return 0;

        return 1;
    }


    private static bool IsLanguageConfigSplitName(string lowerFileName)
    {
        var marker = ".config.";
        var idx = lowerFileName.IndexOf(marker, StringComparison.Ordinal);
        if (idx < 0)
        {
            marker = "split_config.";
            idx = lowerFileName.IndexOf(marker, StringComparison.Ordinal);
            if (idx < 0)
                return false;
        }

        var locale = lowerFileName[(idx + marker.Length)..];
        if (locale.EndsWith(".apk", StringComparison.Ordinal))
            locale = locale[..^4];

        if (locale.Length is < 2 or > 12)
            return false;

        if (locale.Contains("dpi", StringComparison.Ordinal)
            || locale.Contains("arm", StringComparison.Ordinal)
            || locale.Contains("x86", StringComparison.Ordinal))
            return false;

        for (var i = 0; i < locale.Length; i++)
        {
            var c = locale[i];
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '+' or '_')
                continue;
            return false;
        }

        return true;
    }


    private static async Task<byte[]?> ReadMemberFromBundleAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        string member,
        CancellationToken cancellationToken)
    {
        _ = device;
        member = ArchivePath.NormalizeInternal(member);
        if (string.IsNullOrEmpty(member) || apkFiles.Count == 0)
            return null;

        if (CurrentExtractSession.Value is { } session)
            return await session.TryGetFromBundleAsync(apkFiles, member, cancellationToken).ConfigureAwait(false);

        using var fallback = new ApkIconExtractSession(device);
        return await fallback.TryGetFromBundleAsync(apkFiles, member, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Reads <c>resources.arsc</c> from an APK via the active extract session when possible.
    /// </summary>
    private static async Task<byte[]?> TryGetResourcesFromApkAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        CancellationToken cancellationToken)
    {
        if (CurrentExtractSession.Value is { } session)
        {
            var cached = session.TryGetCached(apkPath, RESOURCES);
            if (cached is { Length: > 0 })
                return cached;

            await session.EnsureMembersAsync(apkPath, [RESOURCES], cancellationToken).ConfigureAwait(false);
            return session.TryGetCached(apkPath, RESOURCES);
        }

        return await PullResourcesOnlyAsync(device, apkPath, cancellationToken).ConfigureAwait(false);
    }


    /// <summary>
    /// Drawable paths for a resource id, consulting density-split <c>resources.arsc</c> when the
    /// base table has only a typeSpec (density split owns the file path).
    /// </summary>
    private static async Task<List<string>> ResolveDrawableFilePathsAcrossBundleAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] baseResources,
        int resourceId,
        CancellationToken cancellationToken)
    {
        var paths = ArscResourceResolver.ResolvePaths(baseResources, resourceId)
            .Select(ArchivePath.NormalizeInternal)
            .Where(static p => !string.IsNullOrWhiteSpace(p) && !IsColorResourcePath(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (paths.Count > 0)
            return PreferHighestDensityOnly(paths);

        // Native table empty (INVALID TYPE CONFIG) — density splits own the file. Do not trust
        // AlphaOmega ResourceMap alone; it often invents pool strings for missing configs.
        foreach (var apk in PreferApksForIconMember(apkFiles))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Path.GetFileName(apk).Equals("base.apk", StringComparison.OrdinalIgnoreCase))
                continue;

            var splitRes = await TryGetResourcesFromApkAsync(device, apk, cancellationToken).ConfigureAwait(false);
            if (splitRes is null || splitRes.Length == 0)
                continue;

            paths = ArscResourceResolver.ResolvePaths(splitRes, resourceId)
                .Select(ArchivePath.NormalizeInternal)
                .Where(static p => !string.IsNullOrWhiteSpace(p) && !IsColorResourcePath(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count > 0)
                return PreferHighestDensityOnly(paths);
        }

        return [];
    }


    private static async Task<byte[]?> PullResourcesOnlyAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        CancellationToken cancellationToken
#if DEBUG
        , ApkLoadTiming? timing = null
#endif
        )
    {
        string? stagingRoot = null;
        try
        {
#if DEBUG
            timing?.Mark($"ExtractZipMembersToStaging(resources) {Path.GetFileName(apkPath)}");
#endif
            var (root, contentRoot) = await Task.Run(
                () => ArchiveExtract.ExtractZipMembersToStaging(
                    device.ID, apkPath, [RESOURCES], cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stagingRoot = root;

            await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, RESOURCES), cancellationToken).ConfigureAwait(false);
            var bytes = ToByteArray(stream);
#if DEBUG
            timing?.Mark($"PullResourcesOnlyAsync done ({bytes?.Length ?? 0}B)");
#endif
            return bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
#if DEBUG
            timing?.Mark("PullResourcesOnlyAsync failed");
#endif
            return null;
        }
        finally
        {
            if (stagingRoot is not null)
                ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);
        }
    }


    private static async Task<Dictionary<int, byte[]>> PreloadXmlResourcesAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] resourcesBytes,
        byte[] vectorOrAdaptiveBytes,
        CancellationToken cancellationToken)
    {
        var cache = new Dictionary<int, byte[]>();
        foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(vectorOrAdaptiveBytes))
        {
            if (TryGetResourceColor(new ArscFile(resourcesBytes), resourcesBytes, id) is not null)
                continue;

            foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
            {
                if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;

                var bytes = await ReadMemberFromBundleAsync(device, apkFiles, path, cancellationToken).ConfigureAwait(false);
                if (bytes is { Length: > 0 })
                {
                    cache[id] = bytes;
                    break;
                }
            }
        }

        return cache;
    }


    private static byte[]? ToByteArray(MemoryStream? stream)
    {
        if (stream is null || stream.Length == 0)
            return null;

        stream.Position = 0;
        return stream.ToArray();
    }


    /// <summary>
    /// Read one zip member via the package extract session (shared staging), never via
    /// <see cref="ArchiveExtract.ExtractSelectionForPull"/> (mkdir/mv/cleanup).
    /// </summary>
    private static async Task<byte[]?> ProbeApkMemberBytesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        string member,
        CancellationToken cancellationToken)
    {
        member = ArchivePath.NormalizeInternal(member);
        if (string.IsNullOrEmpty(member))
            return null;

        if (CurrentExtractSession.Value is { } session)
        {
            await session.EnsureMembersAsync(apkPath, [member], cancellationToken).ConfigureAwait(false);
            return session.TryGetCached(apkPath, member);
        }

        using var fallback = new ApkIconExtractSession(device);
        await fallback.EnsureMembersAsync(apkPath, [member], cancellationToken).ConfigureAwait(false);
        return fallback.TryGetCached(apkPath, member);
    }


    private static async Task<List<string>> ResolveIconCandidatesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        byte[] manifestBytes,
        byte[] resourcesBytes,
        CancellationToken cancellationToken
#if DEBUG
        , ApkLoadTiming? timing = null
#endif
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
                #if DEBUG
                timing?.Mark("ResolveIconCandidates: no icon ref → string-pool");
                #endif
                return FindLikelyIconPathsInStringPool(arsc);
            }

            #if DEBUG
            timing?.Mark($"ResolveIconCandidates: iconRef={iconRef}");
            #endif

            // Resolve the manifest icon id only — do not fall back to string-pool or brand
            // guesses here. Density-split APKs often own the real adaptive wrapper while the
            // base arsc lists the id as INVALID. Broad key-hint fallbacks previously
            // matched chrome glyphs and returned notification dots before splits ran.
            var paths = ResolveIconRefToPathsStrict(iconRef, arsc, resourcesBytes);
            if (paths.Count == 0)
            {
                #if DEBUG
                timing?.Mark("ResolveIconCandidates: strict resolve empty");
                #endif
                return [];
            }

            #if DEBUG
            timing?.Mark($"ResolveIconCandidates: {paths.Count} strict paths");
            #endif

            // Adaptive wrappers (anydpi / *launcher* XML) before density rasters.
            var adaptivePreferred = paths
                .Where(IsAdaptiveWrapperPath)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var xmlMember in adaptivePreferred)
            {
                cancellationToken.ThrowIfCancellationRequested();
                #if DEBUG
                timing?.Mark($"probe adaptive wrapper: {xmlMember}");
                #endif
                var xmlBytes = await ProbeApkMemberBytesAsync(device, apkPath, xmlMember, cancellationToken)
                    .ConfigureAwait(false);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                {
                    #if DEBUG
                    timing?.Mark($"adaptive wrapper confirmed: {xmlMember}");
                    #endif
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
                #if DEBUG
                timing?.Mark($"ResolveIconCandidates: {rasters.Count} ranked rasters");
                #endif
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
                #if DEBUG
                timing?.Mark($"probe xml member: {xmlMember}");
                #endif
                var xmlBytes = await ProbeApkMemberBytesAsync(device, apkPath, xmlMember, cancellationToken)
                    .ConfigureAwait(false);
                if (xmlBytes is null || xmlBytes.Length == 0)
                    continue;

                // Keep the adaptive wrapper — never return bare foreground vectors (white-on-transparent).
                if (ApkVectorIconRenderer.IsAdaptiveIcon(xmlBytes))
                {
                    #if DEBUG
                    timing?.Mark($"xml adaptive confirmed: {xmlMember}");
                    #endif
                    return [xmlMember];
                }

                if (ApkVectorIconRenderer.IsVectorDrawable(xmlBytes))
                {
                    #if DEBUG
                    timing?.Mark($"xml vector confirmed: {xmlMember}");
                    #endif
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


    private readonly record struct AdaptiveLayers(
        List<string> ForegroundImages,
        List<List<string>> ForegroundImageLayers,
        List<string> ForegroundXmls,
        List<string> BackgroundImages,
        List<string> BackgroundXmls,
        SKColor? BackgroundColor);


    private static AdaptiveLayers ResolveAdaptiveLayers(byte[] xmlBytes, ArscFile arsc, byte[] resourcesBytes)
    {
        using var stream = new MemoryStream(xmlBytes, writable: false);
        using var axml = new AxmlFile(new StreamLoader(stream));
        if (axml.RootNode is null)
            return new([], [], [], [], [], null);

        var foreground = new List<int>();
        var background = new List<int>();
        var other = new List<int>();
        CollectDrawableResourceIds(axml.RootNode, parentName: null, foreground, background, other);

        (List<string> Images, List<List<string>> ImageLayers, List<string> Xmls, SKColor? Color) ResolveGroup(List<int> ids)
        {
            var images = new List<string>();
            var imageLayers = new List<List<string>>();
            var xmls = new List<string>();
            SKColor? color = null;
            foreach (var id in ids)
            {
                color ??= TryGetResourceColor(arsc, resourcesBytes, id);
                var imagesForId = new List<string>();

                // Prefer native arsc paths. AlphaOmega ResourceMap often returns the wrong
                // string for sparse packages (wrong sibling drawable;
                // color resources mapped to Material state-list XMLs).
                foreach (var path in ResolveDrawableFilePaths(arsc, resourcesBytes, id))
                {
                    if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                    {
                        images.Add(path);
                        imagesForId.Add(path);
                    }
                    else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        xmls.Add(path);
                }

                if (imagesForId.Count > 0)
                    imageLayers.Add(imagesForId.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }

            // Prefer real rasters over animated-vector XML siblings.
            if (images.Count > 0)
                xmls.RemoveAll(x => images.Any(img =>
                    string.Equals(Path.GetFileNameWithoutExtension(img), Path.GetFileNameWithoutExtension(x),
                        StringComparison.OrdinalIgnoreCase)));

            return (
                images.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                imageLayers,
                xmls.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                color);
        }

        var fg = ResolveGroup(foreground.Count > 0 ? foreground : other);
        var bg = ResolveGroup(background);

        // Do not invent string-pool "likely" paths for empty layers — nested adaptive wrappers
        // Broken @drawable layer ids cause compose failures. Empty layers
        // make TryComposeAdaptiveIconAsync return null so density-raster fallback can run.

        return new AdaptiveLayers(fg.Images, fg.ImageLayers, fg.Xmls, bg.Images, bg.Xmls, bg.Color);
    }


    /// <summary>
    /// When adaptive layer drawable ids are missing from the base table (density split owns
    /// the PNGs), resolve those ids across the APK bundle and merge into <paramref name="layers"/>.
    /// </summary>
    private static async Task<AdaptiveLayers> EnrichAdaptiveLayersFromSplitsAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] adaptiveXmlBytes,
        AdaptiveLayers layers,
        byte[] baseResources,
        CancellationToken cancellationToken)
    {
        var needFg = layers.ForegroundImages.Count == 0
                     && layers.ForegroundXmls.Count == 0
                     && layers.ForegroundImageLayers.Count == 0;
        var needBg = layers.BackgroundImages.Count == 0
                     && layers.BackgroundXmls.Count == 0
                     && layers.BackgroundColor is null;
        if (!needFg && !needBg)
            return layers;

        using var stream = new MemoryStream(adaptiveXmlBytes, writable: false);
        using var axml = new AxmlFile(new StreamLoader(stream));
        if (axml.RootNode is null)
            return layers;

        var foreground = new List<int>();
        var background = new List<int>();
        var other = new List<int>();
        CollectDrawableResourceIds(axml.RootNode, parentName: null, foreground, background, other);

        async Task<(List<string> Images, List<List<string>> ImageLayers, List<string> Xmls)> ResolveIdsAsync(
            List<int> ids)
        {
            var images = new List<string>();
            var imageLayers = new List<List<string>>();
            var xmls = new List<string>();
            foreach (var id in ids)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var imagesForId = new List<string>();
                foreach (var path in await ResolveDrawableFilePathsAcrossBundleAsync(
                             device, apkFiles, baseResources, id, cancellationToken)
                             .ConfigureAwait(false))
                {
                    if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                    {
                        images.Add(path);
                        imagesForId.Add(path);
                    }
                    else if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    {
                        xmls.Add(path);
                    }
                }

                if (imagesForId.Count > 0)
                    imageLayers.Add(imagesForId.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
            }

            return (
                images.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                imageLayers,
                xmls.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        var fgImages = layers.ForegroundImages;
        var fgLayers = layers.ForegroundImageLayers;
        var fgXmls = layers.ForegroundXmls;
        if (needFg)
        {
            var fgIds = foreground.Count > 0 ? foreground : other;
            if (fgIds.Count > 0)
            {
                var resolved = await ResolveIdsAsync(fgIds).ConfigureAwait(false);
                if (resolved.Images.Count > 0 || resolved.Xmls.Count > 0)
                {
                    fgImages = resolved.Images;
                    fgLayers = resolved.ImageLayers;
                    fgXmls = resolved.Xmls;
                    #if DEBUG
                    MarkLoadStep($"adaptive fg from density split: {fgImages.Count} img, {fgXmls.Count} xml");
                    #endif
                }
            }
        }

        var bgImages = layers.BackgroundImages;
        var bgXmls = layers.BackgroundXmls;
        var bgColor = layers.BackgroundColor;
        if (needBg && background.Count > 0)
        {
            var resolved = await ResolveIdsAsync(background).ConfigureAwait(false);
            if (resolved.Images.Count > 0 || resolved.Xmls.Count > 0)
            {
                bgImages = resolved.Images;
                bgXmls = resolved.Xmls;
                #if DEBUG
                MarkLoadStep($"adaptive bg from density split: {bgImages.Count} img, {bgXmls.Count} xml");
                #endif
            }
        }

        return new AdaptiveLayers(fgImages, fgLayers, fgXmls, bgImages, bgXmls, bgColor);
    }


    private static SKColor? TryGetResourceColor(ArscFile arsc, byte[] resourcesBytes, int resourceId)
    {
        var framework = ApkVectorIconRenderer.TryResolveAndroidFrameworkColor(resourceId);
        if (framework is not null)
            return framework;

        var native = ArscResourceResolver.ResolveColor(resourcesBytes, resourceId);
        if (native is not null)
            return new SKColor(native.Value);

        if (!arsc.ResourceMap.TryGetValue(resourceId, out var rows) || rows is null)
            return null;

        foreach (var row in rows)
        {
            switch (row.DataType)
            {
                case ArscApi.DATA_TYPE.INT_COLOR_ARGB8:
                case ArscApi.DATA_TYPE.INT_COLOR_RGB8:
                case ArscApi.DATA_TYPE.INT_COLOR_ARGB4:
                case ArscApi.DATA_TYPE.INT_COLOR_RGB4:
                    return new SKColor(unchecked((uint)row.Raw));
            }

            if (!string.IsNullOrWhiteSpace(row.Value)
                && row.Value.StartsWith('#')
                && uint.TryParse(row.Value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            {
                if (row.Value.Length <= 7)
                    hex |= 0xFF000000;
                return new SKColor(hex);
            }
        }

        return null;
    }


    private static async Task<BitmapSource?> TryComposeAdaptiveIconAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] adaptiveXmlBytes,
        ArscFile arsc,
        byte[] resourcesBytes,
        CancellationToken cancellationToken,
        Func<int, byte[]?>? resolveXmlResource = null,
        string? packageName = null)
    {
        try
        {
            return await TryComposeAdaptiveIconCoreAsync(
                device, apkFiles, adaptiveXmlBytes, arsc, resourcesBytes, cancellationToken,
                resolveXmlResource, packageName).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception e)
        {
            // Broken layer resource ids / nested adaptive XML must not
            // abort the whole icon load — density rasters / asset fallbacks are still usable.
#if DEBUG
            MarkLoadStep($"adaptive compose exception: {e.GetType().Name}: {e.Message}");
#else
            _ = e;
#endif
            return null;
        }
    }


    private static async Task<BitmapSource?> TryComposeAdaptiveIconCoreAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] adaptiveXmlBytes,
        ArscFile arsc,
        byte[] resourcesBytes,
        CancellationToken cancellationToken,
        Func<int, byte[]?>? resolveXmlResource,
        string? packageName)
    {
        var layers = ResolveAdaptiveLayers(adaptiveXmlBytes, arsc, resourcesBytes);
        layers = await EnrichAdaptiveLayersFromSplitsAsync(
            device, apkFiles, adaptiveXmlBytes, layers, resourcesBytes, cancellationToken)
            .ConfigureAwait(false);

        if (IsCalendarPackage(packageName))
            layers = SubstituteCalendarDateLayers(layers, resourcesBytes);

        // Final thumbnail size. Layers are kept at ≥108/72 of that so the launcher viewport
        // crop downsamples once instead of downscale-to-192 then upscale×1.5 (blur).
        const int size = 192;
        var layerSize = AdaptiveIconLayerRasterSize(size);
        SKColor? ResolveColor(int id) => TryGetResourceColor(arsc, resourcesBytes, id);

        // Batch-extract adaptive layers once. Prefer highest-density rasters only — pulling every
        // mdpi…xxxhdpi variant wastes sync round-trips.
        var rasterSuspects = PreferHighestDensityOnly(
            layers.ForegroundImages
                .Concat(layers.BackgroundImages)
                .Concat(layers.ForegroundImageLayers.SelectMany(static l => l)));
        var suspectPaths = layers.ForegroundXmls
            .Concat(layers.BackgroundXmls)
            .Concat(rasterSuspects)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (CurrentExtractSession.Value is { } session && suspectPaths.Count > 0)
        {
            #if DEBUG
            MarkLoadStep($"adaptive PrefetchFromBundle ({suspectPaths.Count}): {string.Join(',', suspectPaths)}");
            #endif
            await session.PrefetchFromBundleAsync(apkFiles, suspectPaths, cancellationToken).ConfigureAwait(false);
        }

        // Adaptive wrappers rarely carry fillColor — preload gradients from layer vectors too.
        var xmlCache = new Dictionary<int, byte[]>();
        async Task EnsureFillXmlAsync(byte[]? drawableBytes)
        {
            if (drawableBytes is null || drawableBytes.Length == 0)
                return;

            List<string> fillPaths = [];
            foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(drawableBytes))
            {
                if (xmlCache.ContainsKey(id))
                    continue;
                if (TryGetResourceColor(arsc, resourcesBytes, id) is not null)
                    continue;

                foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
                {
                    if (path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        fillPaths.Add(ArchivePath.NormalizeInternal(path));
                }
            }

            if (fillPaths.Count > 0 && CurrentExtractSession.Value is { } fillSession)
                await fillSession.PrefetchFromBundleAsync(apkFiles, fillPaths, cancellationToken).ConfigureAwait(false);

            foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(drawableBytes))
            {
                if (xmlCache.ContainsKey(id))
                    continue;
                if (TryGetResourceColor(arsc, resourcesBytes, id) is not null)
                    continue;

                foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
                {
                    if (!path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var bytes = await ReadMemberFromBundleAsync(device, apkFiles, path, cancellationToken).ConfigureAwait(false);
                    if (bytes is { Length: > 0 })
                    {
                        xmlCache[id] = bytes;
                        break;
                    }
                }
            }
        }

        await EnsureFillXmlAsync(adaptiveXmlBytes).ConfigureAwait(false);

        foreach (var xmlPath in layers.ForegroundXmls.Concat(layers.BackgroundXmls))
        {
            var layerBytes = await ReadMemberFromBundleAsync(device, apkFiles, xmlPath, cancellationToken).ConfigureAwait(false);
            await EnsureFillXmlAsync(layerBytes).ConfigureAwait(false);
        }

        if (resolveXmlResource is not null)
        {
            // Merge caller cache (if any) without overwriting layer fills we just loaded.
            foreach (var id in ApkVectorIconRenderer.CollectFillResourceIds(adaptiveXmlBytes))
            {
                if (xmlCache.ContainsKey(id))
                    continue;
                var existing = resolveXmlResource(id);
                if (existing is { Length: > 0 })
                    xmlCache[id] = existing;
            }
        }

        resolveXmlResource = id => xmlCache.TryGetValue(id, out var b) ? b : null;

        using var fgLayer = await LoadAdaptiveLayerStackAsync(
            device, apkFiles, layers.ForegroundImageLayers, layers.ForegroundImages, layers.ForegroundXmls,
            layerSize, cancellationToken, ResolveColor, resolveXmlResource, resourcesBytes,
            keepOversizedRaster: true).ConfigureAwait(false);

        using var bgLayer = await LoadAdaptiveLayerAsync(
            device, apkFiles, layers.BackgroundImages, layers.BackgroundXmls, layerSize, cancellationToken,
            ResolveColor, resolveXmlResource, resourcesBytes, keepOversizedRaster: true).ConfigureAwait(false);

        // Inline <vector> under <background>/<foreground> (Clock face; pad under layer-list).
        using var inlineBg = ApkVectorIconRenderer.TryRenderInlineAdaptiveLayer(
            adaptiveXmlBytes, "background", layerSize, SKColors.Transparent, ResolveColor, resolveXmlResource);
        using var inlineFg = ApkVectorIconRenderer.TryRenderInlineAdaptiveLayer(
            adaptiveXmlBytes, "foreground", layerSize, SKColors.Transparent, ResolveColor, resolveXmlResource);

        // Prefer inline artwork when present — drawable siblings are often transparent placeholders
        // (transparent banner placeholders under layer-list).
        var bg = inlineBg ?? bgLayer;
        var fg = inlineFg ?? fgLayer;
        var isClockFace = IsDeskclockPackage(packageName);

        // Live <rotate> hands are not renderable; cache the face only and paint hands at display time.
        if (isClockFace)
            fg = null;
        else if (fg is not null && IsEmptyTransparentLayer(fg))
        {
            // Empty/transparent stock foreground (ic_launcher_foreground is a no-op
            // path) — treat as absent so background-only product art can win.
            // Do NOT use IsDegenerateIcon here: sparse light glyphs
            // valid artwork that only covers a few percent of the canvas.
            fg = null;
        }

        if (bg is null && fg is null && layers.BackgroundColor is null && !isClockFace)
            return null;

        // Background-only is valid when the background carries the launcher art
        // (custom ic_launcher_background + empty foreground).
        // Stock Android Studio green alone is only half the template — use the full default.
        if (fg is null
            && !isClockFace
            && bg is not null
            && IsStockAndroidStudioGreenPlate(bg))
        {
            return DefaultAndroidPackageIcon.Render(size);
        }

        if (fg is null
            && !isClockFace
            && (bg is null || IsDegenerateIcon(bg)))
            return null;

        using var canvasBitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(canvasBitmap);

        var bgColor = layers.BackgroundColor;
        var bgDraw = bg;

        // Drop near-solid white/light adaptive plates (Outlook / Word / Snapseed card).
        // Keep real colored plates (Translate blue). Soft FG alpha veils are APK artwork.
        if (ShouldOmitLightBackgroundForForeground(bgDraw, bgColor, fg))
        {
            bgDraw = null;
            bgColor = null;
        }

        canvas.Clear(bgColor ?? SKColors.Transparent);

        // Crop to the launcher 72/108 viewport only when the layer has clear adaptive-style
        // margins; full-bleed / near-edge art is drawn uncropped.
        if (bgDraw is not null)
            DrawAdaptiveIconLayer(canvas, bgDraw, size);

        if (fg is not null)
        {
            if (!IsCornerBiasedIcon(fg))
            {
                DrawAdaptiveIconLayer(canvas, fg, size);
            }
            else
            {
                using var centeredFg = RecenterOpaqueContent(fg);
                DrawAdaptiveIconLayer(canvas, centeredFg ?? fg, size);
            }
        }

        if (IsDegenerateIcon(canvasBitmap))
            return null;

        return ApkVectorIconRenderer.ToBitmapSource(canvasBitmap);
    }


    private static bool IsCalendarPackage(string? packageName)
        => packageName is not null
           && (packageName.Equals("com.google.android.calendar", StringComparison.OrdinalIgnoreCase)
               || packageName.Equals("com.android.calendar", StringComparison.OrdinalIgnoreCase));


    private static List<string> ResolveCalendarDateIconPaths(byte[] resourcesBytes)
    {
        if (resourcesBytes is null || resourcesBytes.Length == 0)
            return [];

        var day = DateTime.Today.Day;
        var dd = day.ToString("00", CultureInfo.InvariantCulture);
        string[] names =
        [
            $"calendar_date_{dd}_adaptive",
            $"calendar_date_{dd}",
            $"calendar_date_{day.ToString(CultureInfo.InvariantCulture)}",
        ];

        foreach (var name in names)
        {
            var id = ArscResourceResolver.FindResourceIdByKeyName(resourcesBytes, name);
            if (id is null)
                continue;

            var paths = ArscResourceResolver.ResolvePaths(resourcesBytes, id.Value)
                .Select(ArchivePath.NormalizeInternal)
                .Where(static p => !string.IsNullOrWhiteSpace(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count == 0)
                continue;

            var images = paths
                .Where(p => IsImagePath(p) || IsExtensionlessRasterCandidate(p))
                .ToList();
            if (images.Count > 0)
                return PreferHighestDensityOnly(images);

            return PreferIconPaths(paths);
        }

        return [];
    }


    private static HashSet<string> CollectCalendarDateAssetPaths(byte[] resourcesBytes)
    {
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (_, id) in ArscResourceResolver.FindResourceIdsByKeyPrefix(resourcesBytes, "calendar_date_"))
        {
            foreach (var path in ArscResourceResolver.ResolvePaths(resourcesBytes, id))
            {
                var normalized = ArchivePath.NormalizeInternal(path);
                if (!string.IsNullOrWhiteSpace(normalized))
                    paths.Add(normalized);
            }
        }

        return paths;
    }


    /// <summary>
    /// Replaces the store-listing date glyph in the launcher adaptive foreground with today's day-of-month asset.
    /// Does not replace the whole icon — that drawable is only the numeral plate.
    /// </summary>
    private static AdaptiveLayers SubstituteCalendarDateLayers(AdaptiveLayers layers, byte[] resourcesBytes)
    {
        var todayPaths = ResolveCalendarDateIconPaths(resourcesBytes);
        if (todayPaths.Count == 0)
            return layers;

        var allDatePaths = CollectCalendarDateAssetPaths(resourcesBytes);
        if (allDatePaths.Count == 0)
            return layers;

        var todayImages = todayPaths
            .Where(p => IsImagePath(p) || IsExtensionlessRasterCandidate(p))
            .ToList();
        var todayXmls = todayPaths
            .Where(p => p.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var replaced = false;
        var fgLayers = new List<List<string>>();
        foreach (var layer in layers.ForegroundImageLayers)
        {
            if (!layer.Any(allDatePaths.Contains))
            {
                fgLayers.Add(layer);
                continue;
            }

            replaced = true;
            if (todayImages.Count > 0)
                fgLayers.Add(todayImages);
        }

        var fgXmls = layers.ForegroundXmls;
        if (fgXmls.Any(allDatePaths.Contains))
        {
            replaced = true;
            fgXmls = fgXmls
                .Where(p => !allDatePaths.Contains(p))
                .Concat(todayXmls)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else if (replaced && todayImages.Count == 0 && todayXmls.Count > 0)
        {
            fgXmls = fgXmls
                .Concat(todayXmls)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (!replaced && fgLayers.Count > 1 && todayImages.Count > 0)
        {
            fgLayers[^1] = todayImages;
            replaced = true;
        }

        if (!replaced)
            return layers;

        var fgImages = fgLayers
            .SelectMany(static l => l)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return layers with
        {
            ForegroundImages = fgImages,
            ForegroundImageLayers = fgLayers,
            ForegroundXmls = fgXmls,
        };
    }


    /// <summary>
    /// Minimum raster edge for adaptive layers so a 72/108 crop still has ≥ <paramref name="outputSize"/> pixels.
    /// </summary>
    private static int AdaptiveIconLayerRasterSize(int outputSize)
        => Math.Max(outputSize, (int)Math.Ceiling(outputSize * AdaptiveIconLayerDp / AdaptiveIconViewportDp));


    /// <summary>
    /// Draws an adaptive layer into <paramref name="outputSize"/>². Applies the launcher
    /// 72/108 viewport crop only when opaque content is inset (adaptive safe-zone padding);
    /// full-bleed layers are scaled without cropping.
    /// </summary>
    private static void DrawAdaptiveIconLayer(SKCanvas canvas, SKBitmap layer, int outputSize)
    {
        if (ShouldApplyAdaptiveViewportCrop(layer))
            DrawAdaptiveIconViewport(canvas, layer, outputSize);
        else
            canvas.DrawBitmap(layer, new SKRect(0, 0, outputSize, outputSize));
    }


    /// <summary>
    /// True when opaque ink leaves meaningful margin — typical adaptive 108dp layers with
    /// bleed. Edge-reaching / legacy full-bleed art returns false so we do not clip logos.
    /// </summary>
    private static bool ShouldApplyAdaptiveViewportCrop(SKBitmap layer)
    {
        if (layer.Width <= 0 || layer.Height <= 0)
            return false;

        var stride = layer.RowBytes;
        var buffer = new byte[stride * layer.Height];
        System.Runtime.InteropServices.Marshal.Copy(layer.GetPixels(), buffer, 0, buffer.Length);

        var minX = layer.Width;
        var minY = layer.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < layer.Height; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < layer.Width; x += 2)
            {
                if (buffer[row + x * 4 + 3] < 16)
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX)
            return false;

        var fillX = (maxX - minX + 1) / (float)layer.Width;
        var fillY = (maxY - minY + 1) / (float)layer.Height;
        // Only crop clearly padded adaptive layers. Threshold was 0.82 and still clipped
        // logos that use most of the safe zone (Snapseed, system setup icons, etc.).
        return fillX < 0.68f && fillY < 0.68f;
    }


    /// <summary>
    /// Draws the center 72/108 of <paramref name="layer"/> into <paramref name="outputSize"/>²
    /// (one resample; keeps xxxhdpi sharp).
    /// </summary>
    private static void DrawAdaptiveIconViewport(SKCanvas canvas, SKBitmap layer, int outputSize)
    {
        var srcW = layer.Width;
        var srcH = layer.Height;
        if (srcW <= 0 || srcH <= 0 || outputSize <= 0)
            return;

        var visibleW = srcW * AdaptiveIconViewportDp / AdaptiveIconLayerDp;
        var visibleH = srcH * AdaptiveIconViewportDp / AdaptiveIconLayerDp;
        var src = new SKRect(
            (srcW - visibleW) / 2f,
            (srcH - visibleH) / 2f,
            (srcW + visibleW) / 2f,
            (srcH + visibleH) / 2f);
        canvas.DrawBitmap(layer, src, new SKRect(0, 0, outputSize, outputSize));
    }


    private static BitmapSource? CompositeOnOpaqueBackground(SKBitmap foreground, int size, SKColor background)
    {
        using var canvasBitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(canvasBitmap);
        canvas.Clear(background);
        canvas.DrawBitmap(foreground, new SKRect(0, 0, size, size));
        if (IsDegenerateIcon(canvasBitmap))
            return null;
        return ApkVectorIconRenderer.ToBitmapSource(canvasBitmap);
    }


    private static SKBitmap? UpscaleSkBitmap(SKBitmap source, int size)
    {
        if (source.Width == size && source.Height == size)
            return source.Copy();

        return source.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }


    private static async Task<SKBitmap?> LoadAdaptiveLayerStackAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        IReadOnlyList<List<string>> imageLayers,
        IReadOnlyList<string> flatImages,
        IReadOnlyList<string> xmls,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor = null,
        Func<int, byte[]?>? resolveXmlResource = null,
        byte[]? resourcesBytes = null,
        bool keepOversizedRaster = false)
    {
        // Calendar etc.: adaptive foreground is a layer-list of distinct drawables (plate + "31").
        if (imageLayers.Count > 1)
        {
            SKBitmap? composed = null;
            try
            {
                foreach (var layerImages in imageLayers)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    using var layer = await LoadAdaptiveLayerAsync(
                        device, apkFiles, layerImages, [], size, cancellationToken,
                        resolveColor, resolveXmlResource, resourcesBytes, keepOversizedRaster).ConfigureAwait(false);
                    if (layer is null)
                        continue;

                    // Density rasters are often 324²/432² — normalize before stacking.
                    using var sized = EnsureSkBitmapSize(layer, size);
                    if (sized is null)
                        continue;

                    if (composed is null)
                    {
                        composed = sized.Copy();
                        continue;
                    }

                    using var canvas = new SKCanvas(composed);
                    // White-on-black date plates only: punch black when there is substantial
                    // light ink. Dense dark artwork (Sudoku, etc.) must draw as-is.
                    if (IsMostlyDarkPlate(sized) && HasSubstantialLightInk(sized))
                    {
                        using var ink = KnockoutNearBlackKeepLight(sized);
                        if (ink is not null)
                            canvas.DrawBitmap(ink, new SKRect(0, 0, size, size));
                    }
                    else
                    {
                        canvas.DrawBitmap(sized, new SKRect(0, 0, size, size));
                    }
                }

                if (composed is not null)
                    return composed;
            }
            catch
            {
                composed?.Dispose();
                throw;
            }
        }

        return await LoadAdaptiveLayerAsync(
            device, apkFiles, flatImages.Count > 0 ? flatImages : imageLayers.SelectMany(x => x).ToList(),
            xmls, size, cancellationToken, resolveColor, resolveXmlResource, resourcesBytes,
            keepOversizedRaster).ConfigureAwait(false);
    }


    private static SKBitmap? EnsureSkBitmapSize(SKBitmap source, int size)
    {
        if (source.Width == size && source.Height == size)
            return source.Copy();

        return source.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }


    /// <summary>
    /// Keeps xxxhdpi (etc.) intact when composing with a viewport crop; only upscales undersized rasters.
    /// Returns <paramref name="source"/> itself when kept or already exact — caller must not dispose it then.
    /// </summary>
    private static SKBitmap? FitAdaptiveRaster(SKBitmap source, int minSize, bool keepOversized)
    {
        if (source.Width == minSize && source.Height == minSize)
            return source;

        if (keepOversized && source.Width >= minSize && source.Height >= minSize)
            return source;

        return EnsureSkBitmapSize(source, minSize);
    }


    /// <summary>
    /// Treats near-black pixels as transparent so white-on-black date glyphs can overlay a plate.
    /// </summary>
    private static SKBitmap? KnockoutNearBlackKeepLight(SKBitmap source)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return null;

        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        var stride = source.RowBytes;
        var buffer = new byte[stride * source.Height];
        System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), buffer, 0, buffer.Length);

        for (var i = 0; i + 3 < buffer.Length; i += 4)
        {
            var b = buffer[i];
            var g = buffer[i + 1];
            var r = buffer[i + 2];
            var a = buffer[i + 3];
            if (a < 16 || (r < 40 && g < 40 && b < 40))
            {
                buffer[i] = 0;
                buffer[i + 1] = 0;
                buffer[i + 2] = 0;
                buffer[i + 3] = 0;
                continue;
            }

            // Keep light ink (Calendar "31") fully opaque white.
            buffer[i] = 255;
            buffer[i + 1] = 255;
            buffer[i + 2] = 255;
            buffer[i + 3] = a;
        }

        System.Runtime.InteropServices.Marshal.Copy(buffer, 0, result.GetPixels(), buffer.Length);
        return result;
    }


    private static bool IsMostlyDarkPlate(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, dark = 0, light = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;
                var a = buffer[i + 3];
                if (a < 16)
                    continue;

                opaque++;
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                if (r < 40 && g < 40 && b < 40)
                    dark++;
                else if (r > 200 && g > 200 && b > 200)
                    light++;
            }
        }

        return opaque > 0 && dark * 5 >= opaque * 4 && light >= Math.Max(3, opaque / 200);
    }


    /// <summary>
    /// Calendar date plates carry a large share of light ink on the dark plate.
    /// Thin anti-aliased edges on a dark logo must not trigger black knockout.
    /// </summary>
    private static bool HasSubstantialLightInk(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, light = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;
                if (buffer[i + 3] < 16)
                    continue;

                opaque++;
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                if (r > 200 && g > 200 && b > 200)
                    light++;
            }
        }

        return opaque > 0 && light * 5 >= opaque;
    }


    /// <summary>
    /// Near-solid white/light adaptive plates are omitted so brand tiles (Outlook, Word,
    /// Snapseed) are not wrapped in a white card. Kept when the foreground is full-bleed
    /// with interior cutouts that need the plate (Translate letter holes on #EEEEEE).
    /// </summary>
    private static bool ShouldOmitLightBackgroundForForeground(
        SKBitmap? background,
        SKColor? backgroundColor,
        SKBitmap? foreground)
    {
        var lightBg = background is not null && IsNearSolidLightPlate(background)
                      || IsNearWhiteColor(backgroundColor);
        if (!lightBg)
            return false;

        // Translate-style: opaque edges + hollow glyphs — dropping the plate opens dark holes.
        if (foreground is not null && ForegroundNeedsLightPlateBacking(foreground))
            return false;

        return true;
    }


    /// <summary>
    /// True when opaque ink reaches the canvas edge band and the opaque bbox still contains
    /// meaningful transparency (glyph cutouts). Margin-only icons return false.
    /// </summary>
    private static bool ForegroundNeedsLightPlateBacking(SKBitmap foreground)
    {
        if (foreground.Width <= 0 || foreground.Height <= 0)
            return false;

        var stride = foreground.RowBytes;
        var buffer = new byte[stride * foreground.Height];
        System.Runtime.InteropServices.Marshal.Copy(foreground.GetPixels(), buffer, 0, buffer.Length);

        var w = foreground.Width;
        var h = foreground.Height;
        var edgeX = Math.Max(1, w / 12);
        var edgeY = Math.Max(1, h / 12);

        long edgeSamples = 0, edgeOpaque = 0;
        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < h; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < w; x += 2)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                var opaque = buffer[i + 3] >= 16;
                var onEdge = x < edgeX || x >= w - edgeX || y < edgeY || y >= h - edgeY;
                if (onEdge)
                {
                    edgeSamples++;
                    if (opaque)
                        edgeOpaque++;
                }

                if (!opaque)
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (edgeSamples == 0 || maxX < minX)
            return false;

        // Require real full-bleed coverage (Translate blue tile); Outlook glyph fails this.
        if (edgeOpaque * 2 < edgeSamples)
            return false;

        long interior = 0, interiorTransparent = 0;
        for (var y = minY; y <= maxY; y += 2)
        {
            var row = y * stride;
            for (var x = minX; x <= maxX; x += 2)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                interior++;
                if (buffer[i + 3] < 16)
                    interiorTransparent++;
            }
        }

        return interior > 0 && interiorTransparent * 8 >= interior;
    }


    private static bool IsNearWhiteColor(SKColor? color)
    {
        if (color is null)
            return false;

        var c = color.Value;
        return c.Alpha > 200 && c.Red > 245 && c.Green > 245 && c.Blue > 245;
    }


    /// <summary>
    /// Near-solid white / light-gray plate (common adaptive <c>ic_launcher_background</c>).
    /// </summary>
    private static bool IsNearSolidLightPlate(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, light = 0, samples = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                if (buffer[i + 3] < 16)
                    continue;

                opaque++;
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                if (r > 230 && g > 230 && b > 230)
                    light++;
            }
        }

        return samples > 0
               && opaque * 20 >= samples * 19
               && light * 20 >= opaque * 19;
    }


    private static async Task<SKBitmap?> LoadAdaptiveLayerAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        IReadOnlyList<string> images,
        IReadOnlyList<string> xmls,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor = null,
        Func<int, byte[]?>? resolveXmlResource = null,
        byte[]? resourcesBytes = null,
        bool keepOversizedRaster = false)
    {
        // Highest-density first; do not probe every density via archive-path ExtractSelectionForPull.
        var imageCandidates = PreferHighestDensityOnly(RankIconCandidates(images));
        if (imageCandidates.Count > 0)
        {
            if (CurrentExtractSession.Value is { } session)
            {
                await session.PrefetchFromBundleAsync(apkFiles, imageCandidates, cancellationToken)
                    .ConfigureAwait(false);

                foreach (var candidateApk in PreferApksForIconMember(apkFiles))
                {
                    var member = PickBestIconMember(
                        imageCandidates,
                        session.PresentMembers(candidateApk, imageCandidates),
                        m => session.TryGetCached(candidateApk, m)?.Length ?? 0);
                    if (member is null)
                        continue;

                    var cached = session.TryGetCached(candidateApk, member);
                    if (cached is null || cached.Length == 0)
                        continue;

                    var bmp = DecodeSkBitmap(cached);
                    if (bmp is null)
                        continue;

                    var fitted = FitAdaptiveRaster(bmp, size, keepOversizedRaster);
                    if (!ReferenceEquals(fitted, bmp))
                        bmp.Dispose();
                    if (fitted is not null)
                        return fitted;
                }
            }

            foreach (var member in imageCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var bytes = await ReadMemberFromBundleAsync(device, apkFiles, member, cancellationToken)
                    .ConfigureAwait(false);
                if (bytes is null || bytes.Length == 0)
                    continue;

                var bmp = DecodeSkBitmap(bytes);
                if (bmp is null)
                    continue;

                var fitted = FitAdaptiveRaster(bmp, size, keepOversizedRaster);
                if (!ReferenceEquals(fitted, bmp))
                    bmp.Dispose();
                if (fitted is not null)
                    return fitted;
            }
        }

        foreach (var xmlMember in xmls.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var bytes = await ReadMemberFromBundleAsync(device, apkFiles, xmlMember, cancellationToken).ConfigureAwait(false);
            if (bytes is null || bytes.Length == 0)
                continue;

            // Nested adaptive wrappers are not layer drawables (string-pool misfires).
            if (ApkVectorIconRenderer.IsAdaptiveIcon(bytes))
                continue;

            // <color android:color="@color/…"/> solid adaptive backgrounds.
            var colorLayer = ApkVectorIconRenderer.TryRenderColorDrawable(bytes, size, resolveColor);
            if (colorLayer is not null)
                return colorLayer;

            if (ApkVectorIconRenderer.IsVectorDrawable(bytes))
            {
                var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                    bytes, size, background: SKColors.Transparent, resolveColor, resolveXmlResource);
                if (rendered is not null)
                    return rendered;
            }

            // Layer-list: <item android:drawable="@…"/> (often density-split rasters).
            if (resourcesBytes is not null)
            {
                var layerListInner = await TryLoadLayerListDrawableAsync(
                    device, apkFiles, bytes, resourcesBytes, size, cancellationToken,
                    resolveColor, resolveXmlResource).ConfigureAwait(false);
                if (layerListInner is not null)
                    return layerListInner;
            }

            // <inset android:drawable="@…"/> wrapping the real vector.
            if (resourcesBytes is not null)
            {
                var insetInner = await TryLoadInsetDrawableAsync(
                    device, apkFiles, bytes, resourcesBytes, size, cancellationToken,
                    resolveColor, resolveXmlResource).ConfigureAwait(false);
                if (insetInner is not null)
                    return insetInner;
            }

            var gradient = ApkVectorIconRenderer.TryRenderGradientDrawable(bytes, size, resolveColor);
            if (gradient is not null)
                return gradient;
        }

        return null;
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


    private static async Task<SKBitmap?> TryLoadInsetDrawableAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] insetXmlBytes,
        byte[] resourcesBytes,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource)
    {
        try
        {
            using var stream = new MemoryStream(insetXmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            if (axml.RootNode?.NodeName.Equals("inset", StringComparison.OrdinalIgnoreCase) != true)
                return null;

            string? drawableRef = null;
            foreach (var value in EnumerateAllAttributeValues(axml.RootNode)
                         .Concat(EnumerateAttributeValues(axml.RootNode, "drawable")))
            {
                if (value.StartsWith('@'))
                {
                    drawableRef = value;
                    break;
                }
            }

            if (drawableRef is null || !TryParseResourceId(drawableRef, out var id))
                return null;

            // insetLeft/Right/Top/Bottom are parent fractions (often ~18–26%).
            var left = ResolveInsetPixels(axml.RootNode, "insetLeft", size);
            var right = ResolveInsetPixels(axml.RootNode, "insetRight", size);
            var top = ResolveInsetPixels(axml.RootNode, "insetTop", size);
            var bottom = ResolveInsetPixels(axml.RootNode, "insetBottom", size);
            if (left == 0 && right == 0 && top == 0 && bottom == 0)
            {
                var uniform = ResolveInsetPixels(axml.RootNode, "inset", size);
                left = right = top = bottom = uniform;
            }

            var dest = new SKRect(left, top, size - right, size - bottom);
            if (dest.Width < 1 || dest.Height < 1)
                dest = new SKRect(0, 0, size, size);

            SKBitmap? inner = null;
            try
            {
                var cached = resolveXmlResource?.Invoke(id);
                if (cached is { Length: > 0 } && ApkVectorIconRenderer.IsVectorDrawable(cached))
                {
                    inner = ApkVectorIconRenderer.TryRenderToSkBitmap(
                        cached, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                }

                if (inner is null)
                {
                    foreach (var path in await ResolveDrawableFilePathsAcrossBundleAsync(
                                 device, apkFiles, resourcesBytes, id, cancellationToken)
                                 .ConfigureAwait(false))
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var memberBytes = await ReadMemberFromBundleAsync(
                            device, apkFiles, path, cancellationToken).ConfigureAwait(false);
                        if (memberBytes is null || memberBytes.Length == 0)
                            continue;

                        if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                        {
                            inner = DecodeSkBitmap(memberBytes);
                            if (inner is not null)
                                break;
                        }

                        if (ApkVectorIconRenderer.IsVectorDrawable(memberBytes))
                        {
                            inner = ApkVectorIconRenderer.TryRenderToSkBitmap(
                                memberBytes, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                            if (inner is not null)
                                break;
                        }
                    }
                }

                if (inner is null)
                    return null;

                // No effective inset — return the inner drawable as-is.
                if (Math.Abs(dest.Left) < 0.5f && Math.Abs(dest.Top) < 0.5f
                    && Math.Abs(dest.Right - size) < 0.5f && Math.Abs(dest.Bottom - size) < 0.5f)
                {
                    var pass = inner;
                    inner = null;
                    return pass;
                }

                var result = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
                using var canvas = new SKCanvas(result);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(inner, dest);
                return result;
            }
            finally
            {
                inner?.Dispose();
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }


    /// <summary>
    /// Resolves <c>android:inset*</c> to pixels. Supports complex fractions and
    /// plain floats / percentages.
    /// </summary>
    private static float ResolveInsetPixels(XmlNode node, string attributeName, int parentSize)
    {
        foreach (var raw in EnumerateAttributeValues(node, attributeName))
        {
            if (TryParseInsetToPixels(raw, parentSize, out var px))
                return px;
        }

        return 0f;
    }


    private static bool TryParseInsetToPixels(string raw, int parentSize, out float pixels)
    {
        pixels = 0f;
        if (string.IsNullOrWhiteSpace(raw) || parentSize <= 0)
            return false;

        raw = raw.Trim();
        if (raw.EndsWith('%'))
        {
            if (!float.TryParse(raw[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var pct))
                return false;
            pixels = parentSize * (pct / 100f);
            return true;
        }

        // AlphaOmega often emits TYPE_FRACTION as the raw complex int (e.g. 558268976 = 26%).
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits)
            || (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(raw.AsSpan(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out bits)))
        {
            var fraction = ComplexUnitToFloat(unchecked((uint)bits));
            if (fraction > 0f && fraction < 1f)
            {
                pixels = parentSize * fraction;
                return true;
            }

            if (fraction >= 1f && fraction < parentSize)
            {
                pixels = fraction;
                return true;
            }
        }

        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            && float.IsFinite(value))
        {
            if (value > 0f && value < 1f)
            {
                pixels = parentSize * value;
                return true;
            }

            if (value >= 1f && value < parentSize)
            {
                pixels = value;
                return true;
            }
        }

        return false;
    }


    /// <summary>Android <c>TypedValue.complexToFloat</c> for fraction / dimension complex values.</summary>
    private static float ComplexUnitToFloat(uint data)
    {
        var mantissa = (int)((data & 0xFFFFFF00u) >> 8);
        if ((mantissa & 0x800000) != 0)
            mantissa |= unchecked((int)0xFF000000);

        var radix = (data >> 4) & 0xF;
        var mult = radix switch
        {
            0 => 1f,
            1 => 1f / 128f,
            2 => 1f / 32768f,
            3 => 1f / 8388608f,
            _ => 1f,
        };
        return mantissa * mult;
    }


    private static async Task<SKBitmap?> TryLoadLayerListDrawableAsync(
        LogicalDeviceViewModel device,
        IReadOnlyList<string> apkFiles,
        byte[] layerListXmlBytes,
        byte[] resourcesBytes,
        int size,
        CancellationToken cancellationToken,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource)
    {
        try
        {
            using var stream = new MemoryStream(layerListXmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            if (axml.RootNode?.NodeName.Equals("layer-list", StringComparison.OrdinalIgnoreCase) != true)
                return null;

            var drawableIds = new List<int>();
            CollectDrawableResourceIds(axml.RootNode, parentName: null, [], [], drawableIds);
            if (drawableIds.Count == 0)
                return null;

            foreach (var id in drawableIds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var cached = resolveXmlResource?.Invoke(id);
                if (cached is { Length: > 0 } && ApkVectorIconRenderer.IsVectorDrawable(cached))
                {
                    var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                        cached, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                    if (rendered is not null)
                        return rendered;
                }

                var paths = await ResolveDrawableFilePathsAcrossBundleAsync(
                    device, apkFiles, resourcesBytes, id, cancellationToken).ConfigureAwait(false);
                if (paths.Count == 0)
                    continue;

                if (CurrentExtractSession.Value is { } session)
                {
                    await session.PrefetchFromBundleAsync(apkFiles, paths, cancellationToken)
                        .ConfigureAwait(false);
                }

                foreach (var path in paths)
                {
                    var memberBytes = await ReadMemberFromBundleAsync(device, apkFiles, path, cancellationToken)
                        .ConfigureAwait(false);
                    if (memberBytes is null || memberBytes.Length == 0)
                        continue;

                    if (IsImagePath(path) || IsExtensionlessRasterCandidate(path))
                    {
                        var bmp = DecodeSkBitmap(memberBytes);
                        if (bmp is not null)
                            return bmp;
                    }

                    if (ApkVectorIconRenderer.IsVectorDrawable(memberBytes))
                    {
                        var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                            memberBytes, size, SKColors.Transparent, resolveColor, resolveXmlResource);
                        if (rendered is not null)
                            return rendered;
                    }
                }
            }
        }
        catch
        {
            // ignore
        }

        return null;
    }


    /// <summary>
    /// True when opaque ink sits in a corner rather than filling the canvas.
    /// </summary>
    private static bool IsCornerBiasedIcon(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        var minX = bitmap.Width;
        var minY = bitmap.Height;
        var maxX = -1;
        var maxY = -1;
        long opaque = 0;

        for (var y = 0; y < bitmap.Height; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                if (buffer[row + x * 4 + 3] < 16)
                    continue;
                opaque++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (opaque == 0 || maxX < minX)
            return false;

        var bw = maxX - minX + 1;
        var bh = maxY - minY + 1;
        // Content covers most of the canvas — not corner-biased.
        if (bw * 2 >= bitmap.Width && bh * 2 >= bitmap.Height)
            return false;

        var cx = (minX + maxX) / 2f;
        var cy = (minY + maxY) / 2f;
        return Math.Abs(cx - bitmap.Width / 2f) > bitmap.Width * 0.12f
               || Math.Abs(cy - bitmap.Height / 2f) > bitmap.Height * 0.12f;
    }


    /// <summary>
    /// Recenters opaque ink when vector group transforms leave artwork in a corner
    /// corner-biased artwork.
    /// </summary>
    private static SKBitmap? RecenterOpaqueContent(SKBitmap source)
    {
        if (source.Width <= 0 || source.Height <= 0)
            return null;

        var stride = source.RowBytes;
        var buffer = new byte[stride * source.Height];
        System.Runtime.InteropServices.Marshal.Copy(source.GetPixels(), buffer, 0, buffer.Length);

        var minX = source.Width;
        var minY = source.Height;
        var maxX = -1;
        var maxY = -1;

        for (var y = 0; y < source.Height; y++)
        {
            var row = y * stride;
            for (var x = 0; x < source.Width; x++)
            {
                if (buffer[row + x * 4 + 3] < 16)
                    continue;

                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (maxX < minX || maxY < minY)
            return null;

        var contentCx = (minX + maxX) / 2f;
        var contentCy = (minY + maxY) / 2f;
        var canvasCx = source.Width / 2f;
        var canvasCy = source.Height / 2f;
        var dx = canvasCx - contentCx;
        var dy = canvasCy - contentCy;

        // Ignore tiny optical offsets.
        if (Math.Abs(dx) < source.Width * 0.04f && Math.Abs(dy) < source.Height * 0.04f)
            return null;

        var result = new SKBitmap(source.Width, source.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(result);
        canvas.Clear(SKColors.Transparent);
        canvas.DrawBitmap(source, dx, dy);
        return result;
    }


    /// <summary>
    /// Fully empty adaptive layer (stock foreground). Sparse light glyphs that only
    /// cover a few percent of the canvas are still real artwork — use
    /// <see cref="IsDegenerateIcon"/> for blank-tile detection, not for discarding layers.
    /// </summary>
    private static bool IsEmptyTransparentLayer(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, samples = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                if (buffer[i + 3] >= 16)
                    opaque++;
            }
        }

        // <0.25% opaque — empty stock templates, not sparse logos.
        return samples == 0 || opaque * 400 < samples;
    }


    /// <summary>
    /// True when the bitmap is empty/transparent, or a near-solid blank light tile with no real artwork.
    /// White logos on transparency and white-bg icons with color accents are kept.
    /// </summary>
    private static bool IsDegenerateIcon(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return true;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, light = 0, colored = 0, dark = 0, stockGreen = 0, samples = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                var a = buffer[i + 3];
                if (a < 16)
                    continue;

                opaque++;
                if (IsNearStockAndroidStudioGreen(r, g, b))
                    stockGreen++;
                else if (r > 230 && g > 230 && b > 230)
                    light++;
                else if (r < 40 && g < 40 && b < 40)
                    dark++;
                else
                    colored++;
            }
        }

        if (samples == 0 || opaque * 20 < samples) // <5% opaque
            return true;

        // Leftover Android Studio ic_launcher_background (#3DDC84) with no foreground.
        if (opaque * 20 >= samples * 19 && stockGreen * 20 >= opaque * 19)
            return true;

        // Stock Bugdroid foreground alone: mostly transparent, opaque ink near-white.
        if (opaque * 4 < samples
            && light * 10 >= opaque * 9
            && colored < Math.Max(3, opaque / 20)
            && dark < 3
            && stockGreen == 0)
            return true;

        // Real artwork: color accents, dark ink on light tiles, etc.
        if (colored >= Math.Max(3, opaque / 50)
            || dark >= Math.Max(3, opaque / 50))
            return false;

        // Near-solid light fill covering most of the canvas — blank tile.
        return opaque * 2 >= samples && light * 20 >= opaque * 19;
    }


    /// <summary>Near-solid Android Studio template green <c>#3DDC84</c> plate (no Bugdroid).</summary>
    private static bool IsStockAndroidStudioGreenPlate(SKBitmap bitmap)
    {
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, stockGreen = 0, samples = 0;
        for (var y = 0; y < bitmap.Height; y += 4)
        {
            var row = y * stride;
            for (var x = 0; x < bitmap.Width; x += 4)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                if (buffer[i + 3] < 16)
                    continue;

                opaque++;
                if (IsNearStockAndroidStudioGreen(buffer[i + 2], buffer[i + 1], buffer[i]))
                    stockGreen++;
            }
        }

        return samples > 0
               && opaque * 20 >= samples * 19
               && stockGreen * 20 >= opaque * 19;
    }


    /// <summary>Android Studio template green <c>#3DDC84</c> (±slop for resample).</summary>
    private static bool IsNearStockAndroidStudioGreen(byte r, byte g, byte b)
        => Math.Abs(r - 61) <= 28 && Math.Abs(g - 220) <= 28 && Math.Abs(b - 132) <= 28;


    private static SKBitmap? DecodeSkBitmap(byte[] bytes)
    {
        try
        {
            var decoded = SKBitmap.Decode(bytes);
            if (decoded is null)
                return null;

            // Already BGRA — keep AlphaType as-is. ScalePixels Premul→Unpremul invents false
            // near-white samples and made Health Connect omit its white adaptive plate.
            if (decoded.ColorType == SKColorType.Bgra8888)
                return decoded;

            // Solid white adaptive plates often decode as Gray8 (1 byte/px). Pixel scanners and
            // WriteableBitmap assume Bgra8888 — Gray8 caused IndexOutOfRange on Health Connect.
            var converted = new SKBitmap(
                decoded.Width, decoded.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            if (!decoded.ScalePixels(converted, new SKSamplingOptions(SKFilterMode.Nearest, SKMipmapMode.None)))
            {
                using var canvas = new SKCanvas(converted);
                canvas.Clear(SKColors.Transparent);
                canvas.DrawBitmap(decoded, 0, 0);
            }

            decoded.Dispose();
            return converted;
        }
        catch
        {
            return null;
        }
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


    private static string? TryReadPackageLabel(byte[] manifestBytes, byte[] resourcesBytes)
    {
        try
        {
            // Prefer AlphaOmega's named android:label when present — the binary resource-map
            // walker can match a wrong early element (e.g. a settings activity label).
            // Fall back to binary AXML when AO drops the attribute.
            var labelRef = FindNamedApplicationAttribute(manifestBytes, "label")
                           ?? AxmlManifestReader.TryGetApplicationAttribute(
                               manifestBytes, AxmlManifestReader.AttrLabel)
                           ?? FindLauncherActivityLabelFromBytes(manifestBytes);

            if (string.IsNullOrWhiteSpace(labelRef))
                return null;

            labelRef = labelRef.Trim();
            if (!labelRef.StartsWith('@'))
                return IsPlausibleAppLabel(labelRef) ? labelRef : null;

            if (!TryParseResourceId(labelRef, out var resourceId))
                return null;

            // Sparse-aware resolver — AlphaOmega ResourceMap is unreliable for many APKs.
            var resolved = ArscResourceResolver.ResolveString(
                resourcesBytes, resourceId, Data.Settings.ActualUICulture);
            if (!string.IsNullOrWhiteSpace(resolved) && IsPlausibleAppLabel(resolved))
                return resolved;

            // Last resort: Latin-only majority from ResourceMap (ignore non-Latin pollution).
            var arsc = new ArscFile(resourcesBytes);
            if (!arsc.ResourceMap.TryGetValue(resourceId, out var rows) || rows is null || rows.Count == 0)
                return null;

            var candidates = rows
                .Select(r => r.Value?.Trim())
                .Where(v => !string.IsNullOrWhiteSpace(v) && !v.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
                .Cast<string>()
                .ToList();

            return PickBestAppLabel(candidates, requireLatin: true);
        }
        catch
        {
            return null;
        }
    }


    private static string? FindNamedApplicationAttribute(byte[] manifestBytes, string attributeName)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindApplicationAttribute(axml.RootNode, attributeName);
        }
        catch
        {
            return null;
        }
    }


    private static string? FindApplicationAttributeFromAxml(byte[] manifestBytes, string attributeName)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindApplicationAttribute(axml.RootNode, attributeName)
                   ?? (attributeName.Equals("label", StringComparison.OrdinalIgnoreCase)
                       ? FindLauncherActivityLabel(axml.RootNode)
                       : null);
        }
        catch
        {
            return null;
        }
    }


    private static string? FindLauncherActivityLabelFromBytes(byte[] manifestBytes)
    {
        try
        {
            using var manifestStream = new MemoryStream(manifestBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(manifestStream));
            return FindLauncherActivityLabel(axml.RootNode);
        }
        catch
        {
            return null;
        }
    }


    /// <summary>
    /// Some apps omit <c>android:label</c> on <c>&lt;application&gt;</c>
    /// and only label the MAIN/LAUNCHER activity. Also accept nameless <c>@7F12…</c> string refs
    /// when AlphaOmega drops the attribute name.
    /// </summary>
    private static string? FindLauncherActivityLabel(XmlNode? root)
    {
        if (root is null)
            return null;

        foreach (var activity in EnumerateNodesNamed(root, "activity", "activity-alias"))
        {
            if (!ActivityHasLauncherIntent(activity))
                continue;

            var label = GetAttributeValue(activity, "label");
            if (!string.IsNullOrWhiteSpace(label))
                return label;

            // AlphaOmega sometimes drops the attribute name — accept string refs or literals.
            foreach (var value in EnumerateAllAttributeValues(activity))
            {
                if (value.StartsWith('@') && TryParseResourceId(value, out var id))
                {
                    // Prefer string resources (type 0x12 / 0x13 typical) over drawables.
                    var type = (id >> 16) & 0xFF;
                    if (type is >= 0x0B and <= 0x14)
                        return value;
                }

                if (IsPlausibleAppLabel(value)
                    && !value.Contains('.', StringComparison.Ordinal)
                    && !value.StartsWith('@'))
                    return value;
            }
        }

        return null;
    }


    private static bool ActivityHasLauncherIntent(XmlNode activity)
    {
        if (activity.ChildNodes is null)
            return false;

        foreach (var children in activity.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                if (!child.NodeName.Equals("intent-filter", StringComparison.OrdinalIgnoreCase))
                    continue;

                var hasMain = false;
                var hasLauncher = false;
                foreach (var intentChild in EnumerateChildNodes(child))
                {
                    var name = GetAttributeValue(intentChild, "name") ?? "";
                    if (intentChild.NodeName.Equals("action", StringComparison.OrdinalIgnoreCase)
                        && name.Equals("android.intent.action.MAIN", StringComparison.OrdinalIgnoreCase))
                        hasMain = true;
                    if (intentChild.NodeName.Equals("category", StringComparison.OrdinalIgnoreCase)
                        && name.Equals("android.intent.category.LAUNCHER", StringComparison.OrdinalIgnoreCase))
                        hasLauncher = true;
                }

                if (hasMain && hasLauncher)
                    return true;
            }
        }

        return false;
    }


    private static IEnumerable<XmlNode> EnumerateNodesNamed(XmlNode root, params string[] names)
    {
        var set = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var stack = new Stack<XmlNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            if (set.Contains(node.NodeName ?? ""))
                yield return node;

            if (node.ChildNodes is null)
                continue;

            foreach (var children in node.ChildNodes.Values)
            {
                foreach (var child in children)
                    stack.Push(child);
            }
        }
    }


    private static IEnumerable<XmlNode> EnumerateChildNodes(XmlNode node)
    {
        if (node.ChildNodes is null)
            yield break;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
                yield return child;
        }
    }


    private static IEnumerable<string> EnumerateAllAttributeValues(XmlNode node)
    {
        if (node.Attributes is null)
            yield break;

        foreach (var (_, values) in node.Attributes)
        {
            if (values is null)
                continue;
            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }


    /// <summary>
    /// AlphaOmega's ResourceMap often lists every locale (and sometimes polluted values) for one id.
    /// Prefer a stable Latin/default display name and reject format strings / class names.
    /// </summary>
    private static string? PickBestAppLabel(IReadOnlyList<string> candidates, bool requireLatin = false)
    {
        var valid = candidates.Where(IsPlausibleAppLabel).ToList();
        if (valid.Count == 0)
            return null;

        var latin = valid.Where(IsMostlyLatin).ToList();
        if (requireLatin && latin.Count == 0)
            return null;

        var pool = latin.Count > 0 ? latin : valid;

        return pool
            .GroupBy(s => s, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key.Length)
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .FirstOrDefault();
    }


    private static bool IsPlausibleAppLabel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();
        if (value.Length is < 1 or > 80)
            return false;

        // Format strings / placeholders (Play services prompts, etc.)
        if (value.Contains('%', StringComparison.Ordinal))
            return false;

        if (value.StartsWith("res/", StringComparison.OrdinalIgnoreCase))
            return false;

        if (value.Length <= 2)
            return false;

        // Resource typed-value mistakes (e.g. "65536").
        if (value.Length <= 8 && value.All(char.IsAsciiDigit))
            return false;

        // Boolean attrs misread as labels ("true").
        if (value.Equals("true", StringComparison.OrdinalIgnoreCase)
            || value.Equals("false", StringComparison.OrdinalIgnoreCase)
            || value.Equals("yes", StringComparison.OrdinalIgnoreCase)
            || value.Equals("no", StringComparison.OrdinalIgnoreCase))
            return false;

        // Fully-qualified Java/Kotlin type names mistakenly mapped under the label id.
        var dotParts = value.Split('.');
        if (dotParts.Length >= 3
            && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '.' or '_' or '$')
            && dotParts[0] is "com" or "org" or "net" or "io" or "android" or "java" or "kotlin")
            return false;

        if (value.Contains("android.", StringComparison.OrdinalIgnoreCase)
            && value.Contains('.', StringComparison.Ordinal)
            && !value.Contains(' ', StringComparison.Ordinal))
            return false;

        // Do not reject PascalCase tokens — system overlays often ship the resource name
        // as the English label (SetupWizardOverlay). Filtering them sent ResolveString
        // into ResourceMap fishing, which preferred spaced Latin (e.g. Estonian).

        return true;
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
            if (c <= 0x024F) // Basic Latin + Latin Extended
                latin++;
        }

        return letters == 0 || latin * 2 >= letters;
    }


    private static void CollectDrawableResourceIds(
        XmlNode node,
        string? parentName,
        List<int> foreground,
        List<int> background,
        List<int> other)
    {
        var nodeName = node.NodeName ?? "";
        var inFg = IsForegroundContext(nodeName, parentName);
        var inBg = IsBackgroundContext(nodeName, parentName);

        // Named attrs (drawable/src) plus nameless @7F… values AlphaOmega sometimes emits.
        foreach (var value in EnumerateAllAttributeValues(node)
                     .Concat(EnumerateAttributeValues(node, "drawable"))
                     .Concat(EnumerateAttributeValues(node, "src")))
        {
            if (!TryParseResourceId(value, out var id))
                continue;

            if (inFg)
                foreground.Add(id);
            else if (inBg)
                background.Add(id);
            else
                other.Add(id);
        }

        if (node.ChildNodes is null)
            return;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
                CollectDrawableResourceIds(child, nodeName, foreground, background, other);
        }
    }


    private static bool IsForegroundContext(string nodeName, string? parentName)
        => nodeName.Equals("foreground", StringComparison.OrdinalIgnoreCase)
           || (parentName?.Equals("foreground", StringComparison.OrdinalIgnoreCase) ?? false);


    private static bool IsBackgroundContext(string nodeName, string? parentName)
        => nodeName.Equals("background", StringComparison.OrdinalIgnoreCase)
           || (parentName?.Equals("background", StringComparison.OrdinalIgnoreCase) ?? false);


    private static string? FindApplicationAttribute(XmlNode? root, string attributeName)
    {
        if (root is null)
            return null;

        if (root.NodeName.Equals("application", StringComparison.OrdinalIgnoreCase))
            return GetAttributeValue(root, attributeName);

        if (root.ChildNodes is null)
            return null;

        foreach (var children in root.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                var found = FindApplicationAttribute(child, attributeName);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }


    private static string? GetAttributeValue(XmlNode node, string attributeName)
    {
        foreach (var value in EnumerateAttributeValues(node, attributeName))
            return value;

        return null;
    }


    private static IEnumerable<string> EnumerateAttributeValues(XmlNode node, string attributeName)
    {
        if (node.Attributes is null)
            yield break;

        foreach (var (key, values) in node.Attributes)
        {
            if (!AttributeNameMatches(key, attributeName) || values is null)
                continue;

            foreach (var value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }
    }


    private static bool AttributeNameMatches(string key, string attributeName)
    {
        if (key.Equals(attributeName, StringComparison.OrdinalIgnoreCase))
            return true;

        // android:icon / {http://schemas.android.com/apk/res/android}icon
        var suffix = ":" + attributeName;
        return key.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
               || key.EndsWith("}" + attributeName, StringComparison.OrdinalIgnoreCase);
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
        var apkEsc = ADBService.EscapeAdbShellString(apkPath);
        var script =
            $"{unzip} -l {apkEsc} 2>/dev/null | grep -Ei 'res/(mipmap|drawable)[^/]*/[^/]*(launcher|app_icon|ic_launcher)[^/]*\\.(png|webp|xml)$' | head -n 60";

        _ = ADBService.ExecuteDeviceAdbShellCommand(
            deviceId,
            "sh",
            out var stdout,
            out _,
            cancellationToken,
            "-c",
            ADBService.EscapeAdbShellString(script));

        var result = new List<ArchiveEntry>();
        foreach (var rawLine in stdout.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
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
