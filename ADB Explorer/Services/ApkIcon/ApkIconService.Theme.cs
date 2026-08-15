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

    private static void EnsureThemeContrastHook()
    {
        if (_themeContrastHooked)
            return;

        _themeContrastHooked = true;
        ApplicationThemeManager.Changed += (_, _) => App.SafeBeginInvoke(OnAppThemeChanged);
    }


    /// <summary>
    /// Re-applies contrast plates after light/dark switch without re-pulling APKs.
    /// </summary>
    private static void OnAppThemeChanged()
    {
        if (Data.Packages is null || Data.Packages.Count == 0)
            return;

        if (Data.DevicesObject?.Current is not { } device)
            return;

        foreach (var package in Data.Packages)
        {
            if (package.Icon is null || string.IsNullOrEmpty(package.Name))
                continue;

            var refreshed = TryGetStoredIcon(device, package.Name);
            if (refreshed is not null)
                package.Icon = refreshed;
        }
    }


    /// <summary>
    /// Theme-aware presentation: contrast plate for monochrome glyphs, knockout art whose
    /// prevalent ink is too close to the icon-view background, and compact knockout marks.
    /// Disk cache keeps true alpha so theme switches stay correct.
    /// </summary>
    private static BitmapSource? ForDisplay(BitmapSource? source)
    {
        if (source is null)
            return null;

        using var sk = BitmapSourceToSkBitmap(source);
        if (sk is null)
            return source;

        if (TryClassifyMonochromeTransparent(sk, out var isDarkInk)
            || TryClassifyLowContrastTransparent(sk, out isDarkInk)
            || TryClassifyCompactKnockout(sk, out isDarkInk))
        {
            var plated = TryApplyThemeContrastPlate(sk, isDarkInk);
            if (plated is not null)
                return plated;
        }

        return source;
    }


    private static BitmapSource? TryApplyThemeContrastPlate(SKBitmap sk, bool isDarkInk)
    {
        var appIsDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        SKColor? plate = null;
        if (isDarkInk && appIsDark)
            plate = SKColors.White;
        else if (!isDarkInk && !appIsDark)
            plate = new SKColor(0x40, 0x40, 0x40);

        if (plate is null)
            return null;

        using var canvasBitmap = new SKBitmap(sk.Width, sk.Height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(canvasBitmap);
        canvas.Clear(plate.Value);
        canvas.DrawBitmap(sk, 0, 0);
        return ApkVectorIconRenderer.ToBitmapSource(canvasBitmap);
    }


    /// <summary>
    /// Knockout art whose prevalent near-neutral ink disappears against the icon-view
    /// background. A small chromatic accent does not override a large dark or light shape.
    /// </summary>
    private static bool TryClassifyLowContrastTransparent(SKBitmap bitmap, out bool isDarkInk)
    {
        isDarkInk = false;
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        var w = bitmap.Width;
        var h = bitmap.Height;
        if (!CornersAreFullyTransparent(buffer, w, h, stride))
            return false;

        long samples = 0, ink = 0, clear = 0, veil = 0;
        long darkNeutral = 0, lightNeutral = 0;
        long darkR = 0, darkG = 0, darkB = 0;
        long lightR = 0, lightG = 0, lightB = 0;

        for (var y = 0; y < h; y += 2)
        {
            var row = y * stride;
            for (var x = 0; x < w; x += 2)
            {
                var i = row + x * 4;
                if (i + 3 >= buffer.Length)
                    continue;

                samples++;
                var b = buffer[i];
                var g = buffer[i + 1];
                var r = buffer[i + 2];
                var a = buffer[i + 3];

                if (a == 0)
                {
                    clear++;
                    continue;
                }

                if (a < 48)
                {
                    veil++;
                    continue;
                }

                ink++;
                if (ChannelChroma(r, g, b) >= 48)
                    continue;

                var luma = Rec709Luma(r, g, b);
                if (luma <= 115)
                {
                    darkNeutral++;
                    darkR += r;
                    darkG += g;
                    darkB += b;
                }
                else if (luma >= 190)
                {
                    lightNeutral++;
                    lightR += r;
                    lightG += g;
                    lightB += b;
                }
            }
        }

        if (samples == 0 || ink == 0 || clear == 0)
            return false;

        // Translucent fill in the "empty" region — not a knockout background.
        if (veil * 4 >= samples)
            return false;
        if (veil > clear)
            return false;

        var appIsDark = ApplicationThemeManager.GetAppTheme() == ApplicationTheme.Dark;
        SKColor viewBg;
        if (appIsDark)
        {
            if (darkNeutral == 0 || darkNeutral * DominantInkDenominator < ink * DominantInkNumerator)
                return false;

            var prevalent = new SKColor(
                (byte)(darkR / darkNeutral),
                (byte)(darkG / darkNeutral),
                (byte)(darkB / darkNeutral));
            if (!IsTooSimilarToIconViewBackground(prevalent, IconViewBackgroundDark))
                return false;

            isDarkInk = true;
            viewBg = IconViewBackgroundDark;
        }
        else
        {
            if (lightNeutral == 0 || lightNeutral * DominantInkDenominator < ink * DominantInkNumerator)
                return false;

            var lightPrevalent = new SKColor(
                (byte)(lightR / lightNeutral),
                (byte)(lightG / lightNeutral),
                (byte)(lightB / lightNeutral));
            if (!IsTooSimilarToIconViewBackground(lightPrevalent, IconViewBackgroundLight))
                return false;

            isDarkInk = false;
            viewBg = IconViewBackgroundLight;
        }

        if (IsFilledRegularOccupant(buffer, w, h, stride))
            return false;
        if (OutlineAlreadyContrasts(buffer, w, h, stride, viewBg))
            return false;

        return true;
    }


    /// <summary>
    /// Compact knockout mark (opaque bbox under 40% tall and 20% of canvas area).
    /// Treated as dark ink so dark mode paints a white plate; light mode is unchanged.
    /// </summary>
    private static bool TryClassifyCompactKnockout(SKBitmap bitmap, out bool isDarkInk)
    {
        isDarkInk = false;
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        var w = bitmap.Width;
        var h = bitmap.Height;
        if (!CornersAreFullyTransparent(buffer, w, h, stride))
            return false;

        long samples = 0, ink = 0, clear = 0, veil = 0;
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

                samples++;
                var a = buffer[i + 3];
                if (a == 0)
                {
                    clear++;
                    continue;
                }

                if (a < 48)
                {
                    veil++;
                    continue;
                }

                ink++;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (samples == 0 || ink == 0 || clear == 0 || maxX < minX)
            return false;
        if (veil * 4 >= samples || veil > clear)
            return false;

        var bw = maxX - minX + 1;
        var bh = maxY - minY + 1;
        if (bh * 5 >= h * 2)
            return false;
        if ((long)bw * bh * 5 >= (long)w * h)
            return false;

        isDarkInk = true;
        return true;
    }


    /// <summary>
    /// True when opaque ink is one filled square, rounded square, or circle whose width at
    /// half height is at least 70% of the canvas — a plate, not a glyph.
    /// </summary>
    private static bool IsFilledRegularOccupant(byte[] buffer, int w, int h, int stride)
    {
        var mask = new bool[w * h];
        var minX = w;
        var minY = h;
        var maxX = -1;
        var maxY = -1;
        long inkCount = 0;
        var seed = -1;

        for (var y = 0; y < h; y++)
        {
            var row = y * stride;
            for (var x = 0; x < w; x++)
            {
                if (buffer[row + x * 4 + 3] < 48)
                    continue;

                var i = y * w + x;
                mask[i] = true;
                inkCount++;
                if (seed < 0)
                    seed = i;
                if (x < minX) minX = x;
                if (y < minY) minY = y;
                if (x > maxX) maxX = x;
                if (y > maxY) maxY = y;
            }
        }

        if (inkCount == 0 || maxX < minX)
            return false;

        var midY = h / 2;
        var midFirst = -1;
        var midLast = -1;
        var midRow = midY * w;
        for (var x = 0; x < w; x++)
        {
            if (!mask[midRow + x])
                continue;
            if (midFirst < 0)
                midFirst = x;
            midLast = x;
        }

        if (midFirst < 0)
            return false;
        if ((midLast - midFirst + 1) * DominantInkDenominator < w * DominantInkNumerator)
            return false;

        var bw = maxX - minX + 1;
        var bh = maxY - minY + 1;
        var shortSide = Math.Min(bw, bh);
        var longSide = Math.Max(bw, bh);
        if (shortSide * 10 < longSide * 9)
            return false;

        var reached = FloodCount(mask, w, h, seed);
        if (reached * 20 < inkCount * 19)
            return false;

        var bboxArea = (long)bw * bh;
        if (bboxArea <= 0)
            return false;

        var cx = (minX + maxX) * 0.5;
        var cy = (minY + maxY) * 0.5;
        var circleR = shortSide * 0.5;
        var circleRSq = circleR * circleR;
        var cornerR = Math.Max(1, shortSide / 5);

        long matchSquare = 0, matchRound = 0, matchCircle = 0;
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                var on = mask[y * w + x];
                if (on)
                    matchSquare++;

                var dx = x - cx;
                var dy = y - cy;
                var inCircle = dx * dx + dy * dy <= circleRSq;
                if (on == inCircle)
                    matchCircle++;

                var inRound = InRoundedRect(x, y, minX, minY, maxX, maxY, cornerR);
                if (on == inRound)
                    matchRound++;
            }
        }

        var best = matchSquare;
        if (matchRound > best)
            best = matchRound;
        if (matchCircle > best)
            best = matchCircle;

        return best * 10 >= bboxArea * 9;
    }


    /// <summary>
    /// True when at least 90% of the outer silhouette already contrasts with the
    /// icon-view background. Interior holes are ignored.
    /// </summary>
    private static bool OutlineAlreadyContrasts(
        byte[] buffer, int w, int h, int stride, SKColor background)
    {
        var exterior = new bool[w * h];
        MarkExteriorTransparent(buffer, w, h, stride, exterior);

        long outline = 0, contrasting = 0;
        for (var y = 0; y < h; y++)
        {
            var row = y * stride;
            for (var x = 0; x < w; x++)
            {
                var i = row + x * 4;
                if (buffer[i + 3] < 48)
                    continue;
                if (!TouchesExterior(exterior, w, h, x, y))
                    continue;

                outline++;
                var color = new SKColor(buffer[i + 2], buffer[i + 1], buffer[i], buffer[i + 3]);
                if (!IsTooSimilarToIconViewBackground(color, background))
                    contrasting++;
            }
        }

        if (outline == 0)
            return false;

        return contrasting * 10 >= outline * 9;
    }


    private static bool TouchesExterior(bool[] exterior, int w, int h, int x, int y)
    {
        if (x == 0 || y == 0 || x == w - 1 || y == h - 1)
            return true;
        if (exterior[y * w + (x - 1)])
            return true;
        if (exterior[y * w + (x + 1)])
            return true;
        if (exterior[(y - 1) * w + x])
            return true;
        return exterior[(y + 1) * w + x];
    }


    private static void MarkExteriorTransparent(
        byte[] buffer, int w, int h, int stride, bool[] exterior)
    {
        var queue = new Queue<int>();

        void TrySeed(int x, int y)
        {
            if (buffer[y * stride + x * 4 + 3] >= 48)
                return;
            var i = y * w + x;
            if (exterior[i])
                return;
            exterior[i] = true;
            queue.Enqueue(i);
        }

        for (var x = 0; x < w; x++)
        {
            TrySeed(x, 0);
            TrySeed(x, h - 1);
        }

        for (var y = 1; y < h - 1; y++)
        {
            TrySeed(0, y);
            TrySeed(w - 1, y);
        }

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            var x = i % w;
            var y = i / w;
            TryEnqueueExterior(queue, exterior, buffer, w, h, stride, x - 1, y);
            TryEnqueueExterior(queue, exterior, buffer, w, h, stride, x + 1, y);
            TryEnqueueExterior(queue, exterior, buffer, w, h, stride, x, y - 1);
            TryEnqueueExterior(queue, exterior, buffer, w, h, stride, x, y + 1);
        }
    }


    private static void TryEnqueueExterior(
        Queue<int> queue, bool[] exterior, byte[] buffer, int w, int h, int stride, int x, int y)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h)
            return;

        var i = y * w + x;
        if (exterior[i] || buffer[y * stride + x * 4 + 3] >= 48)
            return;

        exterior[i] = true;
        queue.Enqueue(i);
    }


    private static long FloodCount(bool[] ink, int w, int h, int seed)
    {
        var visited = new bool[ink.Length];
        var queue = new Queue<int>();
        queue.Enqueue(seed);
        visited[seed] = true;
        long n = 0;

        while (queue.Count > 0)
        {
            var i = queue.Dequeue();
            if (!ink[i])
                continue;

            n++;
            var x = i % w;
            var y = i / w;
            TryEnqueueFlood(queue, visited, ink, w, h, x - 1, y);
            TryEnqueueFlood(queue, visited, ink, w, h, x + 1, y);
            TryEnqueueFlood(queue, visited, ink, w, h, x, y - 1);
            TryEnqueueFlood(queue, visited, ink, w, h, x, y + 1);
        }

        return n;
    }


    private static void TryEnqueueFlood(
        Queue<int> queue, bool[] visited, bool[] ink, int w, int h, int x, int y)
    {
        if ((uint)x >= (uint)w || (uint)y >= (uint)h)
            return;

        var i = y * w + x;
        if (visited[i] || !ink[i])
            return;

        visited[i] = true;
        queue.Enqueue(i);
    }


    private static bool InRoundedRect(int x, int y, int minX, int minY, int maxX, int maxY, int radius)
    {
        var ix0 = minX + radius;
        var iy0 = minY + radius;
        var ix1 = maxX - radius;
        var iy1 = maxY - radius;
        if (ix0 > ix1 || iy0 > iy1)
            return false;

        if (x >= ix0 && x <= ix1 && y >= minY && y <= maxY)
            return true;
        if (y >= iy0 && y <= iy1 && x >= minX && x <= maxX)
            return true;

        var rSq = radius * radius;
        if (DistSq(x, y, ix0, iy0) <= rSq)
            return true;
        if (DistSq(x, y, ix1, iy0) <= rSq)
            return true;
        if (DistSq(x, y, ix0, iy1) <= rSq)
            return true;
        return DistSq(x, y, ix1, iy1) <= rSq;
    }


    private static int DistSq(int x0, int y0, int x1, int y1)
    {
        var dx = x0 - x1;
        var dy = y0 - y1;
        return dx * dx + dy * dy;
    }


    private static bool IsTooSimilarToIconViewBackground(SKColor color, SKColor background)
    {
        var dr = color.Red - background.Red;
        var dg = color.Green - background.Green;
        var db = color.Blue - background.Blue;
        return dr * dr + dg * dg + db * db <= IconViewBackgroundSimilaritySq;
    }


    private static int ChannelChroma(byte r, byte g, byte b)
    {
        var max = r;
        if (g > max)
            max = g;
        if (b > max)
            max = b;
        var min = r;
        if (g < min)
            min = g;
        if (b < min)
            min = b;
        return max - min;
    }


    private static int Rec709Luma(byte r, byte g, byte b)
        => (r * 54 + g * 183 + b * 19) >> 8;


    private static bool CornersAreFullyTransparent(byte[] buffer, int w, int h, int stride)
    {
        ReadOnlySpan<(int X, int Y)> corners =
        [
            (0, 0),
            (w - 1, 0),
            (0, h - 1),
            (w - 1, h - 1),
        ];

        foreach (var (x, y) in corners)
        {
            var i = y * stride + x * 4;
            if (i + 3 >= buffer.Length || buffer[i + 3] != 0)
                return false;
        }

        return true;
    }


    /// <summary>
    /// True when the bitmap is sparse transparent ink that is almost entirely dark or light
    /// (e.g. logkit). Dense logos and brand art with any real chroma are left alone.
    /// </summary>
    private static bool TryClassifyMonochromeTransparent(SKBitmap bitmap, out bool isDarkInk)
    {
        isDarkInk = false;
        if (bitmap.Width <= 0 || bitmap.Height <= 0)
            return false;

        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        System.Runtime.InteropServices.Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        long opaque = 0, light = 0, dark = 0, colored = 0, samples = 0;
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
                if (r > 230 && g > 230 && b > 230)
                    light++;
                else if (r < 40 && g < 40 && b < 40)
                    dark++;
                else
                    colored++;
            }
        }

        if (samples == 0 || opaque == 0)
            return false;

        // Sparse glyphs only (logkit ~30%). Dense black logos must not get a white plate.
        if (opaque * 5 >= samples * 2) // >= 40% opaque
            return false;

        // Any meaningful chroma → brand art (VLC, Sudoku accents, etc.).
        if (colored > 0 && colored * 50 >= opaque)
            return false;

        if (colored >= 3)
            return false;

        // Require near-pure dark or light ink.
        if (dark * 20 >= opaque * 19)
        {
            isDarkInk = true;
            return true;
        }

        if (light * 20 >= opaque * 19)
        {
            isDarkInk = false;
            return true;
        }

        return false;
    }


    private static SKBitmap? BitmapSourceToSkBitmap(BitmapSource source)
    {
        try
        {
            BitmapSource bgra = source;
            if (source.Format != PixelFormats.Bgra32)
                bgra = new FormatConvertedBitmap(source, PixelFormats.Bgra32, null, 0);

            var width = bgra.PixelWidth;
            var height = bgra.PixelHeight;
            if (width <= 0 || height <= 0)
                return null;

            var stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            var sk = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            System.Runtime.InteropServices.Marshal.Copy(pixels, 0, sk.GetPixels(), pixels.Length);
            return sk;
        }
        catch
        {
            return null;
        }
    }

    private static readonly SKColor IconViewBackgroundDark = new(0x27, 0x27, 0x27);
    private static readonly SKColor IconViewBackgroundLight = new(0xF8, 0xFA, 0xFA);
    private const int IconViewBackgroundSimilaritySq = 120 * 120;
    private const int DominantInkNumerator = 7;
    private const int DominantInkDenominator = 10;
}
