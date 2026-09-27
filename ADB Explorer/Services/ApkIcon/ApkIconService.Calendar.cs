namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
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
}
