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
    /// Icon-view presentation: analog hands at the current local time when the
    /// cached face is a blank disc (Google Clock). OEM faces that already print
    /// hands (ASUS Clock) are shown as-is.
    /// Inspect and CSV writes happen at extract time (or a background catch-up),
    /// not on this getter.
    /// </summary>
    [return: NotNullIfNotNull(nameof(source))]
    public static BitmapSource? ForIconView(BitmapSource? source, string? packageName, string? deviceSerial)
    {
        if (source is null)
            return null;

        if (!IsDeskclockPackage(packageName) || string.IsNullOrEmpty(deviceSerial))
            return source;

        if (!TryGetCachedClockHandsOverlay(deviceSerial, packageName, out var overlay))
        {
            BeginPersistClockHands(deviceSerial, packageName, source);
            return source;
        }

        if (!overlay)
            return source;

        using var sk = BitmapSourceToSkBitmap(source);
        if (sk is null)
            return source;

        using var canvasBitmap = new SKBitmap(sk.Width, sk.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(canvasBitmap);
        canvas.DrawBitmap(sk, 0, 0, PixelCopySampling);
        DrawClockHands(canvas, sk, DateTime.Now);
        return ApkVectorIconRenderer.ToBitmapSource(canvasBitmap);
    }


    private static bool IsDeskclockPackage(string? packageName)
        => !string.IsNullOrEmpty(packageName)
           && (packageName.Contains("deskclock", StringComparison.OrdinalIgnoreCase)
               || packageName.Equals("com.google.android.deskclock", StringComparison.OrdinalIgnoreCase));


    /// <summary>
    /// Analog hands at <paramref name="time"/> (white hour/minute, black second), scaled to the inner face circle.
    /// </summary>
    private static void DrawClockHands(SKCanvas canvas, SKBitmap face, DateTime time)
    {
        var size = face.Width;
        var cx = size / 2f;
        var cy = size / 2f;
        var radius = MeasureClockFaceRadius(face);

        var hour = time.Hour % 12;
        var minute = time.Minute;
        var second = time.Second;
        var hourAngle = hour * 30f + minute * 0.5f;
        var minuteAngle = minute * 6f + second * 0.1f;
        var secondAngle = second * 6f;

        using var paint = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
        };

        DrawClockHand(canvas, cx, cy, hourAngle, radius * 0.50f, radius * 0.12f, radius * 0.06f, paint);
        DrawClockHand(canvas, cx, cy, minuteAngle, radius * 0.78f, radius * 0.085f, radius * 0.06f, paint);

        paint.Color = SKColors.Black;
        DrawClockHand(canvas, cx, cy, secondAngle, radius * 0.90f, radius * 0.035f, radius * 0.18f, paint);

        paint.Color = SKColors.White;
        canvas.DrawCircle(cx, cy, radius * 0.09f, paint);
    }


    /// <summary>
    /// True when the cached clock face already contains 1–3 thin radial hands
    /// (OEM static art). Blank discs used with live overlay return false.
    /// Inspected from the extracted bitmap — once per displayed face.
    /// </summary>
    private static bool ClockFaceAlreadyHasHands(SKBitmap face)
    {
        var w = face.Width;
        var h = face.Height;
        if (w < 16 || h < 16)
            return false;

        var cx = w / 2f;
        var cy = h / 2f;
        var radius = MeasureClockFaceRadius(face);
        var faceColor = SampleClockFaceColor(face, cx, cy, radius);
        if (faceColor.Alpha < 16)
            return false;

        const int binCount = 72;
        const int radialSamples = 10;
        var r0 = radius * 0.22f;
        var r1 = radius * 0.68f;
        if (r1 - r0 < 4f)
            return false;

        Span<float> coverage = stackalloc float[binCount];
        var contrastThresholdSq = 45 * 45;
        for (var b = 0; b < binCount; b++)
        {
            var angle = b * (360f / binCount) * (MathF.PI / 180f);
            var sin = MathF.Sin(angle);
            var cos = MathF.Cos(angle);
            var contrast = 0;
            var total = 0;
            for (var s = 0; s < radialSamples; s++)
            {
                var t = (s + 0.5f) / radialSamples;
                var r = r0 + (r1 - r0) * t;
                var x = (int)MathF.Round(cx + sin * r);
                var y = (int)MathF.Round(cy - cos * r);
                if ((uint)x >= (uint)w || (uint)y >= (uint)h)
                    continue;

                total++;
                var p = face.GetPixel(x, y);
                if (p.Alpha < 16)
                    continue;
                if (RgbDistanceSq(p, faceColor) > contrastThresholdSq)
                    contrast++;
            }

            coverage[b] = total == 0 ? 0f : (float)contrast / total;
        }

        return CountRadialHandPeaks(coverage) is >= 1 and <= 3;
    }


    /// <summary>
    /// Mode of quantized samples on a ring inside the face (hands occupy few angles).
    /// </summary>
    private static SKColor SampleClockFaceColor(SKBitmap face, float cx, float cy, float radius)
    {
        var counts = new Dictionary<uint, int>();
        var r = radius * 0.40f;
        var w = face.Width;
        var h = face.Height;
        for (var deg = 0; deg < 360; deg += 10)
        {
            var rad = deg * (MathF.PI / 180f);
            var x = (int)MathF.Round(cx + MathF.Sin(rad) * r);
            var y = (int)MathF.Round(cy - MathF.Cos(rad) * r);
            if ((uint)x >= (uint)w || (uint)y >= (uint)h)
                continue;

            var p = face.GetPixel(x, y);
            if (p.Alpha < 16)
                continue;

            var key = QuantizeClockColor(p);
            counts[key] = counts.TryGetValue(key, out var n) ? n + 1 : 1;
        }

        uint best = 0;
        var bestCount = -1;
        foreach (var (key, n) in counts)
        {
            if (n <= bestCount)
                continue;
            bestCount = n;
            best = key;
        }

        if (bestCount <= 0)
            return SKColors.Transparent;

        return new SKColor(best);
    }


    private static uint QuantizeClockColor(SKColor p)
        => ((uint)(p.Alpha & 0xF0) << 24)
           | ((uint)(p.Red & 0xF0) << 16)
           | ((uint)(p.Green & 0xF0) << 8)
           | (uint)(p.Blue & 0xF0);


    /// <summary>
    /// Thin radial spikes in the inner ring. Walks from a gap so a hand that
    /// crosses 12 o'clock is one peak, not two.
    /// </summary>
    private static int CountRadialHandPeaks(ReadOnlySpan<float> coverage)
    {
        const float threshold = 0.45f;
        var binCount = coverage.Length;
        var maxWidth = binCount / 8;

        var gap = -1;
        for (var i = 0; i < binCount; i++)
        {
            if (coverage[i] >= threshold)
                continue;
            gap = i;
            break;
        }

        if (gap < 0)
            return 0;

        var peaks = 0;
        var visited = 0;
        var index = gap;
        while (visited < binCount)
        {
            if (coverage[index % binCount] < threshold)
            {
                index++;
                visited++;
                continue;
            }

            var width = 0;
            while (visited < binCount && coverage[index % binCount] >= threshold)
            {
                width++;
                index++;
                visited++;
            }

            if (width >= 2 && width <= maxWidth)
                peaks++;
        }

        return peaks;
    }


    private static void DrawClockHand(
        SKCanvas canvas,
        float cx,
        float cy,
        float angleDegrees,
        float length,
        float width,
        float tail,
        SKPaint paint)
    {
        canvas.Save();
        canvas.Translate(cx, cy);
        canvas.RotateDegrees(angleDegrees);
        canvas.DrawRoundRect(
            new SKRoundRect(new SKRect(-width / 2f, -length, width / 2f, tail), width / 2f),
            paint);
        canvas.Restore();
    }


    /// <summary>
    /// Radius of the inner disc around the center (the analog face), not the outer plate.
    /// </summary>
    private static float MeasureClockFaceRadius(SKBitmap face)
    {
        var w = face.Width;
        var h = face.Height;
        var cx = w / 2;
        var cy = h / 2;
        var maxR = Math.Min(cx, cy);
        var probe = Math.Max(4, maxR / 4);
        var inner = face.GetPixel(Math.Min(w - 1, cx + probe), cy);
        if (inner.Alpha < 16)
            inner = face.GetPixel(cx, Math.Min(h - 1, cy + probe));

        Span<float> hits = stackalloc float[8];
        ReadOnlySpan<(int Dx, int Dy)> dirs =
        [
            (1, 0), (-1, 0), (0, 1), (0, -1),
            (1, 1), (1, -1), (-1, 1), (-1, -1),
        ];

        for (var d = 0; d < dirs.Length; d++)
        {
            var (dx, dy) = dirs[d];
            var hit = (float)maxR;
            var diagonal = dx != 0 && dy != 0;
            for (var i = probe; i < maxR; i++)
            {
                var x = cx + dx * i;
                var y = cy + dy * i;
                if ((uint)x >= (uint)w || (uint)y >= (uint)h)
                    break;

                var p = face.GetPixel(x, y);
                if (p.Alpha < 16 || RgbDistanceSq(p, inner) > 50 * 50)
                {
                    hit = i;
                    if (diagonal)
                        hit *= 1.41421356f;
                    break;
                }
            }

            hits[d] = hit;
        }

        hits.Sort();
        var radius = hits[hits.Length / 2];
        var minRadius = maxR * 0.18f;
        var maxRadius = maxR * 0.92f;
        if (radius < minRadius)
            return minRadius;
        if (radius > maxRadius)
            return maxRadius;
        return radius;
    }


    private static int RgbDistanceSq(SKColor a, SKColor b)
    {
        var dr = a.Red - b.Red;
        var dg = a.Green - b.Green;
        var db = a.Blue - b.Blue;
        return dr * dr + dg * dg + db * db;
    }


    /// <summary>
    /// Live overlay only when this device's clock face is a blank disc.
    /// Stored in <see cref="CSV_FILE"/> field 6, keyed with the icon CRC.
    /// </summary>
    private static bool TryGetCachedClockHandsOverlay(string serial, string packageName, out bool overlay)
    {
        overlay = false;
        lock (GetDeviceLock(serial))
        {
            var cache = GetOrLoadCache(serial);
            if (!cache.TryGetValue(packageName, out var entry)
                || !TryParseClockHandsField(entry.ClockHands, out var cachedBaked))
                return false;

            overlay = !cachedBaked;
            return true;
        }
    }


    private static string? InspectClockHandsField(string? packageName, BitmapSource? bitmap)
    {
        if (!IsDeskclockPackage(packageName) || bitmap is null)
            return null;

        using var sk = BitmapSourceToSkBitmap(bitmap);
        if (sk is null)
            return null;

        return ClockFaceAlreadyHasHands(sk) ? ClockHandsBaked : ClockHandsOverlay;
    }


    /// <summary>
    /// Legacy cache rows without field 6: inspect off the UI thread and persist.
    /// </summary>
    private static void BeginPersistClockHands(string serial, string packageName, BitmapSource source)
    {
        var key = serial + "|" + packageName;
        if (!ClockHandsPersistInFlight.TryAdd(key, 0))
            return;

        var face = source;
        _ = Task.Run(() =>
        {
            try
            {
                lock (GetDeviceLock(serial))
                {
                    var cache = GetOrLoadCache(serial);
                    if (cache.TryGetValue(packageName, out var existing)
                        && TryParseClockHandsField(existing.ClockHands, out _))
                        return;
                }

                var flag = InspectClockHandsField(packageName, face);
                if (flag is null)
                    return;

                lock (GetDeviceLock(serial))
                {
                    var cache = GetOrLoadCache(serial);
                    if (!cache.TryGetValue(packageName, out var entry))
                        return;
                    if (TryParseClockHandsField(entry.ClockHands, out _))
                        return;

                    cache[packageName] = entry with { ClockHands = flag };
                    WriteCache(serial, cache);
                }

                App.SafeBeginInvoke(() =>
                {
                    var pkg = Data.Packages?.FirstOrDefault(p =>
                        p.Name == packageName
                        && (string.IsNullOrEmpty(p.DeviceSerial) || p.DeviceSerial == serial));
                    pkg?.IconViewModel.InvalidateDisplayedIcon();
                });
            }
            catch
            {
                // best-effort catch-up
            }
            finally
            {
                ClockHandsPersistInFlight.TryRemove(key, out _);
            }
        });
    }


    private static string? NormalizeClockHandsField(string field)
    {
        if (string.IsNullOrWhiteSpace(field))
            return null;

        field = field.Trim();
        if (field.Equals(ClockHandsBaked, StringComparison.OrdinalIgnoreCase))
            return ClockHandsBaked;
        if (field.Equals(ClockHandsOverlay, StringComparison.OrdinalIgnoreCase))
            return ClockHandsOverlay;

        return null;
    }


    private static bool TryParseClockHandsField(string? field, out bool hasBakedHands)
    {
        hasBakedHands = false;
        if (string.IsNullOrEmpty(field))
            return false;

        if (field.Equals(ClockHandsBaked, StringComparison.OrdinalIgnoreCase))
        {
            hasBakedHands = true;
            return true;
        }

        if (field.Equals(ClockHandsOverlay, StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }
}
