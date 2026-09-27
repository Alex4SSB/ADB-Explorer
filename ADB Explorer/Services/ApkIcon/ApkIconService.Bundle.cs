using AlphaOmega.Debug;

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
        , ApkLoadTiming? timing = null
        )
    {
        string? stagingRoot = null;
        try
        {
            Mark(timing, $"ExtractZipMembersToStaging(resources) {Path.GetFileName(apkPath)}");
            var (root, contentRoot) = await Task.Run(
                () => ArchiveExtract.ExtractZipMembersToStaging(
                    device.ID, apkPath, [RESOURCES], cancellationToken),
                cancellationToken).ConfigureAwait(false);
            stagingRoot = root;

            await using var stream = await AdbHelper.ReadFileAsStreamAsync(
                device, FileHelper.ConcatPaths(contentRoot, RESOURCES), cancellationToken).ConfigureAwait(false);
            var bytes = ToByteArray(stream);
            Mark(timing, $"PullResourcesOnlyAsync done ({bytes?.Length ?? 0}B)");
            return bytes;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            Mark(timing, "PullResourcesOnlyAsync failed");
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
}
