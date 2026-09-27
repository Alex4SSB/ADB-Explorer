using AlphaOmega.Debug;

using SkiaSharp;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
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
                    MarkLoadStep($"adaptive fg from density split: {fgImages.Count} img, {fgXmls.Count} xml");
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
                MarkLoadStep($"adaptive bg from density split: {bgImages.Count} img, {bgXmls.Count} xml");
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
            MarkLoadStep($"adaptive compose exception: {e.GetType().Name}: {e.Message}");
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
            MarkLoadStep($"adaptive PrefetchFromBundle ({suspectPaths.Count}): {string.Join(',', suspectPaths)}");
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
            canvas.DrawBitmap(layer, new SKRect(0, 0, outputSize, outputSize), ScaleSampling);
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
        canvas.DrawBitmap(layer, src, new SKRect(0, 0, outputSize, outputSize), ScaleSampling);
    }

    private static BitmapSource? CompositeOnOpaqueBackground(SKBitmap foreground, int size, SKColor background)
    {
        using var canvasBitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(canvasBitmap);
        canvas.Clear(background);
        canvas.DrawBitmap(foreground, new SKRect(0, 0, size, size), ScaleSampling);
        if (IsDegenerateIcon(canvasBitmap))
            return null;
        return ApkVectorIconRenderer.ToBitmapSource(canvasBitmap);
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
                            canvas.DrawBitmap(ink, new SKRect(0, 0, size, size), ScaleSampling);
                    }
                    else
                    {
                        canvas.DrawBitmap(sized, new SKRect(0, 0, size, size), ScaleSampling);
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
                canvas.DrawBitmap(inner, dest, ScaleSampling);
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
}
