using SkiaSharp;

namespace ADB_Explorer.Services;

public static partial class ApkIconService
{
    private static SKBitmap? UpscaleSkBitmap(SKBitmap source, int size)
    {
        if (source.Width == size && source.Height == size)
            return source.Copy();

        return source.Resize(new SKImageInfo(size, size), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
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
        canvas.DrawBitmap(source, dx, dy, PixelCopySampling);
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
                canvas.DrawBitmap(decoded, 0, 0, PixelCopySampling);
            }

            decoded.Dispose();
            return converted;
        }
        catch
        {
            return null;
        }
    }
}
