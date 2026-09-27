using AlphaOmega.Debug;
using AlphaOmega.Debug.Manifest;
using SkiaSharp;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
    private const int MaxIconCandidatesToProbe = 12;

    private static readonly string[] ImageExtensions = [".png", ".webp", ".jpg", ".jpeg"];

    private static readonly string[] DensityOrder =
    [
        "xxxhdpi", "xxhdpi", "xhdpi", "hdpi", "tvdpi", "mdpi", "ldpi",
    ];

    private static async Task<string?> LoadLabelAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serial = device.SerialNumber;
        var today = DateOnly.FromDateTime(DateTime.Today);
        packageName ??= TryResolvePackageName(apkPath);

        if (!string.IsNullOrEmpty(packageName) && !NeedsLabelFetch(device, packageName))
            return packageName;

        var manifestListingTask = Task.Run(
            () => ArchiveListing.FetchZipMemberListing(device.ID, apkPath, [MANIFEST], cancellationToken),
            cancellationToken);
        var metaTask = PullManifestAndResourcesAsync(device, apkPath, cancellationToken);
        await Task.WhenAll(manifestListingTask, metaTask).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();

        var manifestEntry = FindEntry(manifestListingTask.Result, MANIFEST);
        var manifestCrc = manifestEntry is null || string.IsNullOrEmpty(manifestEntry.Value.Crc)
            ? ""
            : NormalizeCrc(manifestEntry.Value.Crc);

        var (manifestBytes, resourcesBytes) = metaTask.Result;
        if (manifestBytes is null || manifestBytes.Length == 0 || resourcesBytes is null || resourcesBytes.Length == 0)
        {
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, manifestCrc, today, iconExt: null, label: FailMarker);
            return packageName;
        }

        packageName ??= TryReadPackageName(manifestBytes, resourcesBytes);
        if (string.IsNullOrEmpty(packageName))
            return null;

        var label = TryReadPackageLabel(manifestBytes, resourcesBytes);
        MarkFetchResult(serial, packageName, manifestCrc, today, iconExt: null, label: label ?? FailMarker);
        return packageName;
    }

    private static async Task<(BitmapSource? Icon, string? PackageName)> LoadIconAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        string? packageName,
        CancellationToken cancellationToken
        , ApkLoadTiming? timing = null
        )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serial = device.SerialNumber;
        var deviceId = device.ID;
        var today = DateOnly.FromDateTime(DateTime.Today);

        packageName ??= TryResolvePackageName(apkPath);
        // Force-reload timing must never short-circuit on a cache that a cancelled load re-warmed.
        if (timing is not null)
        {
            Mark(timing, "skip warm/fail cache (force-reload measurement)");
        }
        else
        if (!string.IsNullOrEmpty(packageName))
        {
            lock (GetDeviceLock(serial))
            {
                var cache = GetOrLoadCache(serial);
                if (cache.TryGetValue(packageName, out var warm)
                    && warm.CheckedDate == today
                    && IsSuccessfulIconExt(warm.IconExt))
                {
                    var warmPath = GetLocalIconPath(serial, packageName, warm.IconExt);
                    if (File.Exists(warmPath))
                    {
                        Mark(timing, "warm cache hit (local icon file)");
                        return (ForDisplay(DecodeBitmap(warmPath)), packageName);
                    }
                }

                if (cache.TryGetValue(packageName, out warm)
                    && IsSettledIconMiss(warm, packageName))
                {
                    Mark(timing, "fail-marker cache hit (skip)");
                    return (null, packageName);
                }
            }
        }

        using var extractSession = new ApkIconExtractSession(device);
        CurrentExtractSession.Value = extractSession;
        try
        {
        // Manifest CRC listing in parallel with batch extract of AndroidManifest.xml + resources.arsc.
        Mark(timing, "parallel: zip listing(manifest) + batch extract manifest+arsc");
        var listingSw = Stopwatch.StartNew();
        var metaSw = Stopwatch.StartNew();
        var manifestListingTask = Task.Run(
            () =>
            {
                var listing = ArchiveListing.FetchZipMemberListing(deviceId, apkPath, [MANIFEST], cancellationToken);
                Mark(timing, $"FetchZipMemberListing(AndroidManifest.xml) finished in {listingSw.ElapsedMilliseconds} ms");
                return listing;
            },
            cancellationToken);

        await extractSession.EnsureMembersAsync(apkPath, [MANIFEST, RESOURCES], cancellationToken).ConfigureAwait(false);
        Mark(timing, $"batch manifest+arsc done in {metaSw.ElapsedMilliseconds} ms");

        await manifestListingTask.ConfigureAwait(false);
        Mark(timing, "parallel: zip listing + meta pull both complete");
        cancellationToken.ThrowIfCancellationRequested();

        var manifestEntry = FindEntry(manifestListingTask.Result, MANIFEST);
        if (manifestEntry is null || string.IsNullOrEmpty(manifestEntry.Value.Crc))
        {
            Mark(timing, "manifest CRC missing — abort");
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, "", today, FailMarker, FailMarker);
            return (null, packageName);
        }

        var manifestCrc = NormalizeCrc(manifestEntry.Value.Crc);
        var manifestBytes = extractSession.TryGetCached(apkPath, MANIFEST);
        var resourcesBytes = extractSession.TryGetCached(apkPath, RESOURCES);
        if (manifestBytes is null || manifestBytes.Length == 0 || resourcesBytes is null || resourcesBytes.Length == 0)
        {
            Mark(timing, $"meta pull empty (manifest={manifestBytes?.Length ?? 0}B, arsc={resourcesBytes?.Length ?? 0}B)");
            if (!string.IsNullOrEmpty(packageName))
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, FailMarker);
            return (null, packageName);
        }

        Mark(timing, $"parse package name + label (manifest={manifestBytes.Length}B, arsc={resourcesBytes.Length}B)");
        packageName ??= TryReadPackageName(manifestBytes, resourcesBytes);
        if (string.IsNullOrEmpty(packageName))
        {
            Mark(timing, "package name unresolved — abort");
            return (null, null);
        }

        var label = TryReadPackageLabel(manifestBytes, resourcesBytes) ?? FailMarker;
        Mark(timing, $"label={(label == FailMarker ? "fail" : "ok")}");

        // Persist the label before icon work so a compose crash still leaves the display name.
        if (label != FailMarker)
            MarkFetchResult(serial, packageName, manifestCrc, today, iconExt: null, label);

        lock (GetDeviceLock(serial))
        {
            var cache = GetOrLoadCache(serial);
            if (
                timing is null &&
                cache.TryGetValue(packageName, out var existing)
                && IsSettledIconMiss(existing, packageName))
            {
                var missLabel = MergeLocaleLabel(existing.Label, label);
                var missUpdated = existing with
                {
                    ManifestCrc = string.IsNullOrEmpty(manifestCrc) ? existing.ManifestCrc : manifestCrc,
                    CheckedDate = today,
                    Label = missLabel,
                };
                if (!string.Equals(existing.ManifestCrc, missUpdated.ManifestCrc, StringComparison.OrdinalIgnoreCase))
                    missUpdated = missUpdated with { ClockHands = null };
                if (existing.CheckedDate != today
                    || missUpdated.Label != existing.Label
                    || missUpdated.ManifestCrc != existing.ManifestCrc)
                {
                    cache[packageName] = missUpdated;
                    WriteCache(serial, cache);
                }

                Mark(timing, "settled miss — skip icon member extract");
                return (null, packageName);
            }

            if (
                timing is null &&
                cache.TryGetValue(packageName, out existing)
                && string.Equals(existing.ManifestCrc, manifestCrc, StringComparison.OrdinalIgnoreCase)
                && IsSuccessfulIconExt(existing.IconExt)
                && !IsCalendarPackage(packageName))
            {
                var localPath = GetLocalIconPath(serial, packageName, existing.IconExt);
                if (File.Exists(localPath))
                {
                    // Append/replace only the current UI locale; keep other locales already in the CSV.
                    var updatedLabel = MergeLocaleLabel(existing.Label, label);
                    var updated = existing with
                    {
                        CheckedDate = today,
                        Label = updatedLabel,
                    };
                    if (existing.CheckedDate != today || updated.Label != existing.Label)
                    {
                        cache[packageName] = updated;
                        WriteCache(serial, cache);
                    }

                    Mark(timing, "CRC-matched local icon — skip re-extract");
                    return (ForDisplay(DecodeBitmap(localPath)), packageName);
                }
            }
            else if (timing is not null
                && cache.TryGetValue(packageName, out var crcHit)
                && string.Equals(crcHit.ManifestCrc, manifestCrc, StringComparison.OrdinalIgnoreCase)
                && IsSuccessfulIconExt(crcHit.IconExt)
                && File.Exists(GetLocalIconPath(serial, packageName, crcHit.IconExt)))
            {
                Mark(timing, "CRC would match local icon — forcing re-extract for timing");
            }
        }

        Mark(timing, "ResolveIconCandidatesAsync start");
        var iconCandidates = await ResolveIconCandidatesAsync(
            device, apkPath, manifestBytes, resourcesBytes, cancellationToken
            , timing
            ).ConfigureAwait(false);
        Mark(timing, $"ResolveIconCandidatesAsync done ({iconCandidates.Count} candidates)");

        Mark(timing, "DiscoverApkBundleFiles start");
        var apkFiles = DiscoverApkBundleFiles(deviceId, apkPath);
        Mark(timing, $"DiscoverApkBundleFiles done ({apkFiles.Count} apk(s))");
        byte[] effectiveResources = resourcesBytes;

        if (iconCandidates.Count == 0 && apkFiles.Count > 1)
        {
            foreach (var splitApk in PreferApksForRead(apkFiles, apkPath).Where(p => !string.Equals(p, apkPath, StringComparison.Ordinal)))
            {
                cancellationToken.ThrowIfCancellationRequested();
                Mark(timing, $"PullResourcesOnlyAsync split: {Path.GetFileName(splitApk)}");
                var splitResources = await PullResourcesOnlyAsync(device, splitApk, cancellationToken
                    , timing
                    ).ConfigureAwait(false);
                if (splitResources is null || splitResources.Length == 0)
                    continue;

                Mark(timing, $"ResolveIconCandidatesAsync on split {Path.GetFileName(splitApk)}");
                var splitCandidates = await ResolveIconCandidatesAsync(
                    device, splitApk, manifestBytes, splitResources, cancellationToken
                    , timing
                    ).ConfigureAwait(false);
                if (splitCandidates.Count == 0)
                    continue;

                iconCandidates = splitCandidates;
                effectiveResources = splitResources;
                Mark(timing, $"split candidates found ({iconCandidates.Count})");
                break;
            }
        }

        if (iconCandidates.Count == 0)
        {
            // Splits exhausted (or single APK) — string-pool / heuristics.
            // Overlay / RRO arsc is often tiny or malformed; AlphaOmega ArscFile can throw
            // (Resolve already swallowed that). Keep fallbacks best-effort.
            Mark(timing, "fallback string-pool / heuristic candidates");
            try
            {
                iconCandidates = FindLikelyIconPathsInStringPool(new ArscFile(effectiveResources));
                if (iconCandidates.Count == 0)
                    iconCandidates = HeuristicIconCandidates();
            }
            catch (Exception e)
            {
                Mark(timing, $"fallback parse failed: {e.GetType().Name}: {e.Message}");
                iconCandidates = [];
            }

            Mark(timing, $"fallback candidates: {iconCandidates.Count}");
        }

        // Stock ic_launcher paths are last-resort only. Prepending them used to beat a confirmed
        // brand adaptive wrapper because both score as adaptive and
        // PickBestIconMember's stable sort keeps earlier list order — composing the leftover
        // Android Studio template instead of the real icon.
        if (!iconCandidates.Any(IsAdaptiveWrapperPath))
        {
            iconCandidates =
            [
                .. iconCandidates,
                "res/mipmap-anydpi-v26/ic_launcher.xml",
                "res/drawable-anydpi-v26/ic_launcher.xml",
                "res/mipmap-anydpi-v26/ic_launcher_round.xml",
                "res/drawable-anydpi-v26/ic_launcher_round.xml",
            ];
        }

        string? iconMember = null;
        var iconSourceApk = apkPath;
        if (iconCandidates.Count > 0)
        {
            if (iconCandidates.Count > MaxIconCandidatesToProbe)
                iconCandidates = iconCandidates.Take(MaxIconCandidatesToProbe).ToList();

            // Prefer base/density only; Prefetch stops once every candidate is found.
            Mark(timing, $"Prefetch icon candidates ({iconCandidates.Count})");
            await extractSession.PrefetchFromBundleAsync(apkFiles, iconCandidates, cancellationToken)
                .ConfigureAwait(false);

            foreach (var candidateApk in PreferApksForIconMember(apkFiles, apkPath))
            {
                iconMember = PickBestIconMember(
                    iconCandidates,
                    extractSession.PresentMembers(candidateApk, iconCandidates),
                    m => extractSession.TryGetCached(candidateApk, m)?.Length ?? 0);
                if (iconMember is not null)
                {
                    iconSourceApk = candidateApk;
                    Mark(timing, $"picked icon member: {iconMember} from {Path.GetFileName(candidateApk)}");
                    break;
                }
            }
        }

        if (iconMember is null)
        {
            foreach (var candidateApk in PreferApksForRead(apkFiles, apkPath))
            {
                Mark(timing, $"DiscoverIconMembersOnDevice: {Path.GetFileName(candidateApk)}");
                var discovered = DiscoverIconMembersOnDevice(deviceId, candidateApk, cancellationToken);
                if (discovered.Count == 0)
                    continue;

                // Keep adaptive XML — RankIconCandidates is raster-only and would drop wrappers.
                iconCandidates = RankDiscoveredIconCandidates(discovered.Select(e => e.Path));
                iconMember = PickBestIconMember(iconCandidates, discovered);
                if (iconMember is not null)
                {
                    iconSourceApk = candidateApk;
                    Mark(timing, $"discovered icon member: {iconMember}");
                    await extractSession.EnsureMembersAsync(candidateApk, [iconMember], cancellationToken)
                        .ConfigureAwait(false);
                    break;
                }
            }
        }

        if (iconMember is null)
        {
            Mark(timing, "no icon member found — abort");
            MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
            return (null, packageName);
        }

        Mark(timing, $"ReadFileAsStreamAsync icon: {iconMember}");
        var memberBytes = extractSession.TryGetCached(iconSourceApk, iconMember);
        if (memberBytes is null || memberBytes.Length == 0)
        {
            await extractSession.EnsureMembersAsync(iconSourceApk, [iconMember], cancellationToken)
                .ConfigureAwait(false);
            memberBytes = extractSession.TryGetCached(iconSourceApk, iconMember);
        }

        if (memberBytes is null || memberBytes.Length == 0)
        {
            Mark(timing, "icon bytes empty — abort");
            MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
            return (null, packageName);
        }

        cancellationToken.ThrowIfCancellationRequested();

        Mark(timing, $"icon bytes ready ({memberBytes.Length}B, xml={iconMember.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)})");

        BitmapSource? bitmap = null;
        string iconExt;
        var writeRawRaster = false;
        var resourcesForIcon = effectiveResources;

        if (iconMember.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
        {
            SKColor? ResolveColor(int id)
            {
                var arsc = new ArscFile(resourcesForIcon);
                return TryGetResourceColor(arsc, resourcesForIcon, id);
            }

            // Prefer base arsc for layer refs (adaptive XML may be in a density split, vectors in base).
            var composeResources = resourcesBytes;
            var composeArsc = new ArscFile(composeResources);
            var apkBundle = PreferApksForRead(apkFiles, apkPath);

            Dictionary<int, byte[]>? xmlCache = null;
            async Task<Dictionary<int, byte[]>> GetXmlCacheAsync()
            {
                if (xmlCache is not null)
                    return xmlCache;

                Mark(timing, "PreloadXmlResourcesAsync start");
                xmlCache = await PreloadXmlResourcesAsync(
                    device, apkBundle, composeResources, memberBytes, cancellationToken).ConfigureAwait(false);
                Mark(timing, $"PreloadXmlResourcesAsync done ({xmlCache.Count} entries)");
                return xmlCache;
            }

            Func<int, byte[]?> ResolveXml = id =>
                xmlCache is not null && xmlCache.TryGetValue(id, out var bytes) ? bytes : null;

            if (ApkVectorIconRenderer.IsVectorDrawable(memberBytes))
            {
                Mark(timing, "render vector drawable");
                xmlCache = await PreloadXmlResourcesAsync(
                    device, apkBundle, resourcesForIcon, memberBytes, cancellationToken).ConfigureAwait(false);
                Mark(timing, $"PreloadXmlResourcesAsync (vector) done ({xmlCache.Count})");

                using var rendered = ApkVectorIconRenderer.TryRenderToSkBitmap(
                    memberBytes, size: 192, background: SKColors.Transparent,
                    resolveColor: ResolveColor, resolveXmlResource: ResolveXml);
                if (rendered is not null && IsStockAndroidStudioGreenPlate(rendered))
                {
                    // Green plate without Bugdroid — ship the complete template icon.
                    bitmap = DefaultAndroidPackageIcon.Render(192);
                    Mark(timing, "stock AS green plate → default package icon");
                }
                else if (rendered is not null && !IsDegenerateIcon(rendered))
                {
                    // Corner-biased vectors need recentering; full launcher vectors must keep placement.
                    if (IsCornerBiasedIcon(rendered))
                    {
                        using var centered = RecenterOpaqueContent(rendered);
                        bitmap = ApkVectorIconRenderer.ToBitmapSource(centered ?? rendered);
                    }
                    else
                    {
                        bitmap = ApkVectorIconRenderer.ToBitmapSource(rendered);
                    }
                    Mark(timing, "vector render ok");
                }
                else
                {
                    Mark(timing, "vector render failed / degenerate");
                }
            }
            else
            {
                await GetXmlCacheAsync().ConfigureAwait(false);
                Mark(timing, "TryComposeAdaptiveIconAsync start");
                bitmap = await TryComposeAdaptiveIconAsync(
                    device, apkBundle, memberBytes, composeArsc, composeResources, cancellationToken,
                    ResolveXml, packageName).ConfigureAwait(false);
                Mark(timing, bitmap is null ? "adaptive compose failed" : "adaptive compose ok");

                // If adaptive composition fails, fall back to the best foreground raster
                // composited on white (Android adaptive icons are always opaque).
                if (bitmap is null)
                {
                    Mark(timing, "adaptive fg-raster fallback start");
                    var layers = ResolveAdaptiveLayers(memberBytes, composeArsc, composeResources);
                    var fgOnly = RankIconCandidates(layers.ForegroundImages);
                    if (fgOnly.Count == 0)
                    {
                        // Adaptive layers unresolved — use every
                        // density raster for the manifest icon id (PreferIconPaths would keep
                        // only the adaptive XML and RankIconCandidates would then drop it).
                        var iconRef = AxmlManifestReader.TryGetApplicationAttribute(manifestBytes, AxmlManifestReader.AttrIcon)
                                      ?? FindApplicationAttributeFromAxml(manifestBytes, "icon");
                        if (!string.IsNullOrWhiteSpace(iconRef))
                        {
                            fgOnly = RankIconCandidates(ResolveIconRefToAllPaths(iconRef, composeArsc, composeResources));

                            // Density-split arsc often owns the legacy PNG while base only
                            // lists the adaptive XML for the same icon id.
                            if (TryParseResourceId(iconRef, out var iconId))
                            {
                                foreach (var splitApk in PreferApksForIconMember(apkBundle, apkPath))
                                {
                                    if (Path.GetFileName(splitApk)
                                        .Equals("base.apk", StringComparison.OrdinalIgnoreCase))
                                        continue;

                                    var splitRes = await TryGetResourcesFromApkAsync(
                                        device, splitApk, cancellationToken).ConfigureAwait(false);
                                    if (splitRes is null || splitRes.Length == 0)
                                        continue;

                                    var splitPaths = RankIconCandidates(
                                        ArscResourceResolver.ResolvePaths(splitRes, iconId));
                                    if (splitPaths.Count == 0)
                                        continue;

                                    fgOnly = RankIconCandidates(fgOnly.Concat(splitPaths));
                                    Mark(timing, $"fg-raster split paths: {splitPaths.Count}");
                                    break;
                                }
                            }
                        }
                    }

                    if (fgOnly.Count > 0)
                    {
                        var fgProbe = fgOnly.Take(MaxIconCandidatesToProbe).ToList();
                        if (CurrentExtractSession.Value is { } fgSession)
                            await fgSession.PrefetchFromBundleAsync(apkBundle, fgProbe, cancellationToken)
                                .ConfigureAwait(false);

                        foreach (var candidateApk in PreferApksForIconMember(apkBundle, apkPath))
                        {
                            Mark(timing, $"fg batch in {Path.GetFileName(candidateApk)}");
                            string? fgMember;
                            if (CurrentExtractSession.Value is { } s)
                            {
                                fgMember = PickBestIconMember(
                                    fgProbe,
                                    s.PresentMembers(candidateApk, fgProbe),
                                    m => s.TryGetCached(candidateApk, m)?.Length ?? 0);
                            }
                            else
                            {
                                var fgListing = ArchiveListing.FetchZipMemberListing(
                                    deviceId, candidateApk, fgProbe, cancellationToken);
                                fgMember = PickBestIconMember(fgOnly, fgListing);
                            }

                            if (fgMember is null)
                                continue;

                            Mark(timing, $"read fg raster: {fgMember}");
                            var fgBytes = CurrentExtractSession.Value?.TryGetCached(candidateApk, fgMember);
                            if (fgBytes is null || fgBytes.Length == 0)
                            {
                                fgBytes = await ProbeApkMemberBytesAsync(
                                    device, candidateApk, fgMember, cancellationToken).ConfigureAwait(false);
                            }

                            if (fgBytes is null || fgBytes.Length == 0)
                                continue;

                            using var fgSk = DecodeSkBitmap(fgBytes);
                            if (fgSk is null)
                                continue;

                            // Keep declared non-white colors. Drop near-white so light FG ink
                            // (VLC) is not washed out; raster white plates are omitted in compose.
                            var bgColor = layers.BackgroundColor ?? SKColors.Transparent;
                            if (IsNearWhiteColor(bgColor))
                                bgColor = SKColors.Transparent;

                            bitmap = CompositeOnOpaqueBackground(fgSk, 192, bgColor);
                            if (bitmap is not null)
                            {
                                Mark(timing, "fg-raster composite ok");
                                break;
                            }

                            memberBytes = fgBytes;
                            iconMember = fgMember;
                            writeRawRaster = true;
                            Mark(timing, "fg-raster write-raw fallback");
                            break;
                        }
                    }
                    else
                    {
                        Mark(timing, "no fg-raster candidates");
                    }
                }
            }

            // Density-split apps: adaptive fg is often a layer-list whose rasters live only in a split.
            if (bitmap is null && !writeRawRaster)
            {
                // RankIconCandidates is xxxhdpi-first; Take(N) alone never reaches xxhdpi-only packs.
                const int heuristicProbe = 48;
                var heuristic = HeuristicIconProbeCandidates(heuristicProbe);
                Mark(timing, $"heuristic probe ({heuristic.Count} paths)");
                if (CurrentExtractSession.Value is { } heurSession)
                    await heurSession.PrefetchFromBundleAsync(apkBundle, heuristic, cancellationToken)
                        .ConfigureAwait(false);

                foreach (var candidateApk in PreferApksForIconMember(apkBundle, apkPath))
                {
                    string? member;
                    if (CurrentExtractSession.Value is { } s)
                    {
                        member = PickBestIconMember(
                            heuristic,
                            s.PresentMembers(candidateApk, heuristic),
                            m => s.TryGetCached(candidateApk, m)?.Length ?? 0);
                    }
                    else
                    {
                        var listing = ArchiveListing.FetchZipMemberListing(
                            deviceId, candidateApk, heuristic, cancellationToken);
                        member = PickBestIconMember(heuristic, listing);
                    }

                    if (member is null)
                        continue;

                    Mark(timing, $"heuristic hit: {member} in {Path.GetFileName(candidateApk)}");
                    var bytes = CurrentExtractSession.Value?.TryGetCached(candidateApk, member);
                    if (bytes is null || bytes.Length == 0)
                    {
                        bytes = await ProbeApkMemberBytesAsync(
                            device, candidateApk, member, cancellationToken).ConfigureAwait(false);
                    }

                    if (bytes is null || bytes.Length == 0)
                        continue;

                    using var sk = DecodeSkBitmap(bytes);
                    if (sk is null || IsDegenerateIcon(sk))
                        continue;

                    bitmap = CompositeOnOpaqueBackground(sk, 192, SKColors.Transparent);
                    if (bitmap is not null)
                    {
                        Mark(timing, "heuristic composite ok");
                        break;
                    }

                    memberBytes = bytes;
                    iconMember = member;
                    writeRawRaster = true;
                    Mark(timing, "heuristic write-raw fallback");
                    break;
                }
            }


            if (bitmap is null && !writeRawRaster)
            {
                Mark(timing, "XML icon path exhausted — abort");
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            if (writeRawRaster)
            {
                iconExt = Path.GetExtension(iconMember);
                if (string.IsNullOrEmpty(iconExt))
                    iconExt = DetectRasterExtension(memberBytes) ?? ".png";
            }
            else
            {
                iconExt = ".png";
            }
        }
        else
        {
            iconExt = Path.GetExtension(iconMember);
            if (string.IsNullOrEmpty(iconExt))
                iconExt = DetectRasterExtension(memberBytes) ?? ".png";
            writeRawRaster = true;
            Mark(timing, $"raw raster path ({iconExt})");
        }

        // Extensionless WebP (WebView res/9M) must not be saved as .png — WIC then decodes
        // VP8L without alpha and fills transparent corners with opaque black.
        if (writeRawRaster
            && iconExt.Equals(".png", StringComparison.OrdinalIgnoreCase)
            && DetectRasterExtension(memberBytes) is { } detected
            && !detected.Equals(".png", StringComparison.OrdinalIgnoreCase))
        {
            iconExt = detected;
            Mark(timing, $"corrected raster ext → {iconExt}");
        }

        Mark(timing, "save local icon file");
        var localDir = GetLocalIconDirectory(serial);
        Directory.CreateDirectory(localDir);
        var localFile = GetLocalIconPath(serial, packageName, iconExt);

        if (bitmap is not null && !writeRawRaster)
        {
            await SaveBitmapAsPngAsync(bitmap, localFile, cancellationToken).ConfigureAwait(false);
            Mark(timing, "SaveBitmapAsPngAsync done");
        }
        else
        {
            // Upscale tiny system rasters so list thumbnails are not muddy.
            using (var rawSk = DecodeSkBitmap(memberBytes))
            {
                if (rawSk is not null && (rawSk.Width < 128 || rawSk.Height < 128))
                {
                    var upscaled = UpscaleSkBitmap(rawSk, 192);
                    if (upscaled is not null)
                    {
                        try
                        {
                            bitmap = ApkVectorIconRenderer.ToBitmapSource(upscaled);
                            var pngPath = GetLocalIconPath(serial, packageName, ".png");
                            await SaveBitmapAsPngAsync(bitmap, pngPath, cancellationToken).ConfigureAwait(false);
                            if (!string.Equals(pngPath, localFile, StringComparison.OrdinalIgnoreCase)
                                && File.Exists(localFile))
                            {
                                try { File.Delete(localFile); } catch { /* ignore */ }
                            }

                            MarkFetchResult(serial, packageName, manifestCrc, today, ".png", label,
                                InspectClockHandsField(packageName, bitmap));
                            Mark(timing, "upscaled tiny raster saved");
                            return (ForDisplay(bitmap), packageName);
                        }
                        finally
                        {
                            upscaled.Dispose();
                        }
                    }
                }
            }

            await using (var fs = new FileStream(localFile, FileMode.Create, FileAccess.Write, FileShare.Read))
                await fs.WriteAsync(memberBytes, cancellationToken).ConfigureAwait(false);

            if (!File.Exists(localFile) || new FileInfo(localFile).Length == 0)
            {
                try { File.Delete(localFile); } catch { /* ignore */ }
                Mark(timing, "local file write failed — abort");
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            bitmap = DecodeBitmap(localFile);
            if (bitmap is null)
            {
                try { File.Delete(localFile); } catch { /* ignore */ }
                Mark(timing, "DecodeBitmap failed — abort");
                MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                return (null, packageName);
            }

            // Reject near-solid white / empty rasters (adaptive fg-only leftovers, etc.).
            using (var sk = DecodeSkBitmap(memberBytes))
            {
                if (sk is not null && IsDegenerateIcon(sk))
                {
                    try { File.Delete(localFile); } catch { /* ignore */ }
                    Mark(timing, "degenerate icon rejected — abort");
                    MarkFetchResult(serial, packageName, manifestCrc, today, FailMarker, label);
                    return (null, packageName);
                }
            }

            Mark(timing, "raw raster saved + decoded");
        }

        MarkFetchResult(serial, packageName, manifestCrc, today, iconExt, label,
            InspectClockHandsField(packageName, bitmap));
        Mark(timing, "MarkFetchResult / cache write done");
        return (ForDisplay(bitmap), packageName);
        }
        finally
        {
            CurrentExtractSession.Value = null;
        }
    }

    /// <param name="iconExt">New icon ext, <see cref="FailMarker"/>, or null to leave the existing icon field unchanged.</param>
    /// <param name="label">New label, <see cref="FailMarker"/>, or null to leave the existing label unchanged.</param>
    /// <param name="clockHands">
    /// Deskclock inspect result (<c>baked</c>/<c>overlay</c>), or null to keep/clear with CRC.
    /// </param>
    private static void MarkFetchResult(
        string serial,
        string packageName,
        string manifestCrc,
        DateOnly today,
        string? iconExt,
        string? label,
        string? clockHands = null)
    {
        lock (GetDeviceLock(serial))
        {
            var cache = GetOrLoadCache(serial);
            cache.TryGetValue(packageName, out var existing);

            var nextIcon = iconExt ?? existing.IconExt ?? "";
            string? nextLabel;
            if (label is null)
            {
                nextLabel = existing.Label;
            }
            else
            {
                var existingLabel = existing.Label == FailMarker ? null : existing.Label;
                nextLabel = MergeLocaleLabel(existingLabel, label);
            }
            var nextCrc = string.IsNullOrEmpty(manifestCrc) ? (existing.ManifestCrc ?? "") : manifestCrc;
            string? nextHands;
            if (clockHands is not null)
                nextHands = clockHands;
            else if (!string.Equals(existing.ManifestCrc, nextCrc, StringComparison.OrdinalIgnoreCase))
                nextHands = null;
            else
                nextHands = existing.ClockHands;

            // Drop obsolete local files when the extension changes.
            if (IsSuccessfulIconExt(existing.IconExt)
                && IsSuccessfulIconExt(nextIcon)
                && !string.Equals(existing.IconExt, nextIcon, StringComparison.OrdinalIgnoreCase))
            {
                var oldPath = GetLocalIconPath(serial, packageName, existing.IconExt);
                try { if (File.Exists(oldPath)) File.Delete(oldPath); } catch { /* ignore */ }
            }

            cache[packageName] = new ApkIconCacheEntry(nextCrc, today, nextIcon, nextLabel, nextHands);
            WriteCache(serial, cache);
        }
    }

    private static async Task<(byte[]? Manifest, byte[]? Resources)> PullManifestAndResourcesAsync(
        LogicalDeviceViewModel device,
        string apkPath,
        CancellationToken cancellationToken
        , ApkLoadTiming? timing = null
        , Stopwatch? outerSw = null
        )
    {
        string? stagingRoot = null;
        try
        {
            Mark(timing, "ExtractZipMembersToStaging(manifest+arsc) start");
            var extractSw = Stopwatch.StartNew();
            var (root, contentRoot) = await Task.Run(
                () => ArchiveExtract.ExtractZipMembersToStaging(
                    device.ID, apkPath, [MANIFEST, RESOURCES], cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stagingRoot = root;
            Mark(timing, $"ExtractZipMembersToStaging done in {extractSw.ElapsedMilliseconds} ms");

            Mark(timing, "ReadFileAsStreamAsync manifest + resources start");
            var readSw = Stopwatch.StartNew();
            var manifestTask = AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, MANIFEST), cancellationToken);
            var resourcesTask = AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, RESOURCES), cancellationToken);

            await Task.WhenAll(manifestTask, resourcesTask).ConfigureAwait(false);
            Mark(timing, $"ReadFileAsStreamAsync manifest+resources done in {readSw.ElapsedMilliseconds} ms"
                + (outerSw is null ? "" : $" (meta wall {outerSw.ElapsedMilliseconds} ms)"));

            return (ToByteArray(manifestTask.Result), ToByteArray(resourcesTask.Result));
        }
        catch (OperationCanceledException)
        {
            Mark(timing, "PullManifestAndResources cancelled");
            throw;
        }
        catch (Exception e)
        {
            Mark(timing, $"PullManifestAndResources failed: {e.Message}");
#if !DEPLOY
            DebugLog.PrintLine($"APK meta pull failed for {apkPath}: {e.Message}");
#endif
            return (null, null);
        }
        finally
        {
            if (stagingRoot is not null)
            {
                Mark(timing, "CleanupStaging start");
                ArchiveExtract.CleanupStaging(device.ID, stagingRoot, CancellationToken.None);
                Mark(timing, "CleanupStaging done");
            }
        }
    }

    /// <summary>
    /// Sibling APKs in an app install dir (<c>base.apk</c> + <c>split_*.apk</c>).
    /// Shared folders such as <c>/product/overlay</c> hold many unrelated RROs — do not
    /// treat them as density splits (that previously scanned dozens of APKs per icon load).
    /// </summary>
    private static List<string> DiscoverApkBundleFiles(string deviceId, string apkPath)
    {
        var result = new List<string> { apkPath };
        try
        {
            var parent = FileHelper.GetParentPath(apkPath);
            if (string.IsNullOrEmpty(parent))
                return result;

            var siblings = new List<(string Name, string Full)>();
            foreach (var entry in AdbService.ListDirectoryEntries(deviceId, parent, CancellationToken.None))
            {
                if (entry.Type is not AbstractFile.FileType.File)
                    continue;

                var name = entry.FullName ?? "";
                if (!name.EndsWith(".apk", StringComparison.OrdinalIgnoreCase))
                    continue;

                var full = string.IsNullOrEmpty(entry.FullPath)
                    ? FileHelper.ConcatPaths(parent, name)
                    : entry.FullPath;
                siblings.Add((name, full));
            }

            // Play/system install dirs always include base.apk. Standalone APKs in shared
            // dirs (overlays, priv-app dumps) use distinct filenames and must stay alone.
            var selfName = Path.GetFileName(apkPath);
            var hasBase = selfName.Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                || siblings.Any(s => s.Name.Equals("base.apk", StringComparison.OrdinalIgnoreCase));
            if (!hasBase)
                return result;

            foreach (var (name, full) in siblings)
            {
                if (!name.Equals("base.apk", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("split_", StringComparison.OrdinalIgnoreCase))
                    continue;

                if (result.Any(p => string.Equals(p, full, StringComparison.Ordinal)))
                    continue;

                result.Add(full);
            }
        }
        catch
        {
            // Keep the single base path.
        }

        return result;
    }

    private const int IconApkRankBase = 35;

    /// <summary>
    /// Adaptive layer size in dp (full bleed including mask padding).
    /// </summary>
    private const float AdaptiveIconLayerDp = 108f;

    /// <summary>
    /// Launcher-visible viewport in dp (<c>AdaptiveIconDrawable</c> uses 72 = 108×2/3).
    /// </summary>
    private const float AdaptiveIconViewportDp = 72f;

    private static async Task SaveBitmapAsPngAsync(BitmapSource bitmap, string path, CancellationToken cancellationToken)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        await using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read);
        encoder.Save(fs);
        await fs.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    [GeneratedRegex(@"^\s*(?<Length>\d+)\s+\d{4}-\d{2}-\d{2}\s+\d{2}:\d{2}\s+(?<Name>.+\S)\s*$", RegexOptions.CultureInvariant)]
    private static partial Regex UnzipListEntryLine();
}
