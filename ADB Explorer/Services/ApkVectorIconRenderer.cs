using AlphaOmega.Debug;
using SkiaSharp;

namespace ADB_Explorer.Services;

/// <summary>
/// Renders simple Android vector drawables (path-based) to a bitmap.
/// Used when an APK launcher icon is adaptive/vector-only (e.g. Termux).
/// </summary>
internal static partial class ApkVectorIconRenderer
{
    private const int DefaultSize = 192;

    public static bool IsVectorDrawable(byte[] axmlBytes)
    {
        try
        {
            using var stream = new MemoryStream(axmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            // Adaptive icons often nest <vector> under background (Clock) — not a vector drawable.
            if (axml.RootNode?.NodeName.Equals("adaptive-icon", StringComparison.OrdinalIgnoreCase) == true)
                return false;
            return FindVectorRoot(axml.RootNode) is not null;
        }
        catch
        {
            return false;
        }
    }

    public static bool IsAdaptiveIcon(byte[] axmlBytes)
    {
        try
        {
            using var stream = new MemoryStream(axmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            return axml.RootNode?.NodeName.Equals("adaptive-icon", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch
        {
            return false;
        }
    }

    public static BitmapSource? TryRender(
        byte[] axmlBytes,
        int size = DefaultSize,
        SKColor? background = null,
        Func<int, SKColor?>? resolveColor = null,
        Func<int, byte[]?>? resolveXmlResource = null)
    {
        using var sk = TryRenderToSkBitmap(axmlBytes, size, background, resolveColor, resolveXmlResource);
        return sk is null ? null : ToBitmapSource(sk);
    }

    /// <summary>Caller owns and must dispose the returned bitmap.</summary>
    public static SKBitmap? TryRenderToSkBitmap(
        byte[] axmlBytes,
        int size = DefaultSize,
        SKColor? background = null,
        Func<int, SKColor?>? resolveColor = null,
        Func<int, byte[]?>? resolveXmlResource = null)
    {
        try
        {
            using var stream = new MemoryStream(axmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            var root = FindVectorRoot(axml.RootNode);
            if (root is null)
                return null;

            return RenderVectorNode(root, axmlBytes, size, background, resolveColor, resolveXmlResource);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Renders a standalone <c>&lt;gradient&gt;</c> drawable (Truecaller/Canon launcher fills)
    /// as a full-size bitmap.
    /// </summary>
    public static SKBitmap? TryRenderGradientDrawable(byte[] axmlBytes, int size = DefaultSize)
    {
        try
        {
            using var stream = new MemoryStream(axmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            var gradient = FindGradientRoot(axml.RootNode);
            if (gradient is null)
                return null;

            using var shader = TryCreateGradientShader(gradient, size, size);
            if (shader is null)
                return null;

            var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
            using var canvas = new SKCanvas(bitmap);
            using var paint = new SKPaint { IsAntialias = true, Shader = shader };
            canvas.DrawRect(0, 0, size, size, paint);
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Renders an inline <c>&lt;vector&gt;</c> nested under an adaptive-icon layer
    /// (e.g. Clock background; Calculator pad under a foreground layer-list) when the
    /// layer has no usable drawable/src raster.
    /// </summary>
    public static SKBitmap? TryRenderInlineAdaptiveLayer(
        byte[] adaptiveXmlBytes,
        string layerName,
        int size = DefaultSize,
        SKColor? background = null,
        Func<int, SKColor?>? resolveColor = null,
        Func<int, byte[]?>? resolveXmlResource = null)
    {
        try
        {
            using var stream = new MemoryStream(adaptiveXmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            var layer = FindNamedChild(axml.RootNode, layerName);
            var vector = FindVectorRoot(layer);
            if (vector is null)
                return null;

            return RenderVectorNode(vector, adaptiveXmlBytes, size, background, resolveColor, resolveXmlResource);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>True when <paramref name="layerName"/> contains a nested <c>&lt;vector&gt;</c>.</summary>
    public static bool HasInlineVectorUnderLayer(byte[] adaptiveXmlBytes, string layerName)
    {
        try
        {
            using var stream = new MemoryStream(adaptiveXmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            var layer = FindNamedChild(axml.RootNode, layerName);
            return FindVectorRoot(layer) is not null;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Collects <c>@resource</c> / raw resource-id fill references for preloading gradient XML.</summary>
    public static List<int> CollectFillResourceIds(byte[] axmlBytes)
    {
        var ids = new List<int>();
        try
        {
            using var stream = new MemoryStream(axmlBytes, writable: false);
            using var axml = new AxmlFile(new StreamLoader(stream));
            CollectFillResourceIds(axml.RootNode, ids);
        }
        catch
        {
            // ignore
        }

        return ids.Distinct().ToList();
    }

    public static BitmapSource ToBitmapSource(SKBitmap bitmap)
    {
        var stride = bitmap.RowBytes;
        var buffer = new byte[stride * bitmap.Height];
        Marshal.Copy(bitmap.GetPixels(), buffer, 0, buffer.Length);

        var writeable = new WriteableBitmap(bitmap.Width, bitmap.Height, 96, 96, PixelFormats.Bgra32, null);
        writeable.WritePixels(new Int32Rect(0, 0, bitmap.Width, bitmap.Height), buffer, stride, 0);
        writeable.Freeze();
        return writeable;
    }

    /// <summary>Common <c>android.R.color.*</c> values referenced from adaptive icons (package 0x01).</summary>
    public static SKColor? TryResolveAndroidFrameworkColor(int resourceId)
    {
        if (((resourceId >> 24) & 0xFF) != 0x01)
            return null;

        return (resourceId & 0xFFFF) switch
        {
            0x000B => SKColors.White,       // android.R.color.white
            0x000C => SKColors.Black,       // android.R.color.black
            0x000D => SKColors.Transparent, // android.R.color.transparent
            0x0000 => new SKColor(0xFFAAAAAA), // darker_gray
            _ => null,
        };
    }

    private static SKBitmap? RenderVectorNode(
        XmlNode root,
        byte[] axmlBytes,
        int size,
        SKColor? background,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource)
    {
        var viewportWidth = ReadFloatAttribute(root, "viewportWidth") ?? 24f;
        var viewportHeight = ReadFloatAttribute(root, "viewportHeight") ?? 24f;
        if (viewportWidth <= 0 || viewportHeight <= 0 || viewportWidth > 4096 || viewportHeight > 4096)
            return null;

        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(background ?? SKColors.Transparent);

        var scale = Math.Min(size / viewportWidth, size / viewportHeight);
        var dx = (size - viewportWidth * scale) / 2f;
        var dy = (size - viewportHeight * scale) / 2f;
        canvas.Translate(dx, dy);
        canvas.Scale(scale);

        var drew = DrawNode(root, canvas, axmlBytes, viewportWidth, viewportHeight, resolveColor, resolveXmlResource);
        if (!drew)
        {
            bitmap.Dispose();
            return null;
        }

        return bitmap;
    }

    private static bool DrawNode(
        XmlNode node,
        SKCanvas canvas,
        byte[] axmlBytes,
        float viewportWidth,
        float viewportHeight,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource)
    {
        var nodeName = node.NodeName ?? "";
        if (nodeName.Equals("group", StringComparison.OrdinalIgnoreCase))
        {
            canvas.Save();
            var tx = ReadFloatBits(node, "translateX") ?? 0f;
            var ty = ReadFloatBits(node, "translateY") ?? 0f;
            var sx = ReadFloatBits(node, "scaleX") ?? 1f;
            var sy = ReadFloatBits(node, "scaleY") ?? 1f;
            var px = ReadFloatBits(node, "pivotX") ?? 0f;
            var py = ReadFloatBits(node, "pivotY") ?? 0f;
            var rotation = ReadFloatBits(node, "rotation") ?? 0f;

            // VectorDrawable docs: transforms apply scale → rotate → translate in viewport
            // space about the pivot. (Zoom wordmark: translate = (viewport - scaledWidth) / 2.)
            // Build M = T(translate+pivot) × R × S × T(-pivot) so p' = R(S(p-pivot))+pivot+translate.
            var local = SKMatrix.CreateTranslation(-px, -py);
            if (sx != 1f || sy != 1f)
                local = SKMatrix.Concat(SKMatrix.CreateScale(sx, sy), local);
            if (rotation != 0f)
                local = SKMatrix.Concat(SKMatrix.CreateRotationDegrees(rotation), local);
            if (tx != 0f || ty != 0f || px != 0f || py != 0f)
                local = SKMatrix.Concat(SKMatrix.CreateTranslation(tx + px, ty + py), local);
            canvas.Concat(local);

            var drewGroup = DrawChildren(node, canvas, axmlBytes, viewportWidth, viewportHeight, resolveColor, resolveXmlResource);
            canvas.Restore();
            return drewGroup;
        }

        var drew = false;
        if (nodeName.Equals("path", StringComparison.OrdinalIgnoreCase))
        {
            var pathData = GetAttribute(node, "pathData");
            pathData = AxmlStringPool.ExpandTruncated(axmlBytes, pathData);
            if (!string.IsNullOrWhiteSpace(pathData))
            {
                var cleaned = PathWhitespace().Replace(pathData, " ").Trim();
                using var path = ParseSvgPath(cleaned, axmlBytes, pathData);
                if (path is not null && !path.IsEmpty)
                {
                    var fillTypeRaw = GetAttribute(node, "fillType");
                    if (fillTypeRaw is "1" or "evenOdd"
                        || (int.TryParse(fillTypeRaw, out var fillTypeInt) && fillTypeInt == 1))
                    {
                        path.FillType = SKPathFillType.EvenOdd;
                    }

                    using var paint = new SKPaint
                    {
                        IsAntialias = true,
                        Style = SKPaintStyle.Fill,
                    };

                    SKShader? fillShader = null;
                    try
                    {
                        if (TryApplyFill(node, paint, viewportWidth, viewportHeight, resolveColor, resolveXmlResource, out fillShader))
                        {
                            ApplyAlphaAttribute(node, "fillAlpha", paint);
                            if (paint.Color.Alpha > 0 || paint.Shader is not null)
                            {
                                canvas.DrawPath(path, paint);
                                drew = true;
                            }
                        }
                    }
                    finally
                    {
                        fillShader?.Dispose();
                    }
                }
            }
        }

        drew |= DrawChildren(node, canvas, axmlBytes, viewportWidth, viewportHeight, resolveColor, resolveXmlResource);
        return drew;
    }

    /// <summary>
    /// Parses SVG path data, retrying with a longer string-pool expansion when AlphaOmega
    /// truncated pathData leaves a tiny / empty path (Play Protect gear, etc.).
    /// </summary>
    private static SKPath? ParseSvgPath(string cleaned, byte[] axmlBytes, string? originalPartial)
    {
        var path = SKPath.ParseSvgPathData(cleaned);
        if (path is not null && !path.IsEmpty)
        {
            var bounds = path.Bounds;
            if (bounds.Width >= 2 && bounds.Height >= 2)
                return path;
            path.Dispose();
        }

        if (string.IsNullOrEmpty(originalPartial) || originalPartial.Length < 8)
            return null;

        var expanded = AxmlStringPool.ExpandTruncated(axmlBytes, originalPartial);
        if (string.IsNullOrEmpty(expanded) || expanded.Length <= cleaned.Length)
        {
            // Try every longer pool string that shares the leading command prefix.
            var prefix = originalPartial[..Math.Min(16, originalPartial.Length)];
            string? best = null;
            foreach (var s in AxmlStringPool.ReadAll(axmlBytes))
            {
                if (s.Length <= originalPartial.Length || !s.StartsWith(prefix, StringComparison.Ordinal))
                    continue;
                if (best is null || s.Length > best.Length)
                    best = s;
            }

            expanded = best;
        }

        if (string.IsNullOrEmpty(expanded))
            return null;

        var retry = PathWhitespace().Replace(expanded, " ").Trim();
        var retryPath = SKPath.ParseSvgPathData(retry);
        if (retryPath is null || retryPath.IsEmpty)
        {
            retryPath?.Dispose();
            return null;
        }

        return retryPath;
    }

    private static bool DrawChildren(
        XmlNode node,
        SKCanvas canvas,
        byte[] axmlBytes,
        float viewportWidth,
        float viewportHeight,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource)
    {
        if (node.ChildNodes is null)
            return false;

        var drew = false;
        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
                drew |= DrawNode(child, canvas, axmlBytes, viewportWidth, viewportHeight, resolveColor, resolveXmlResource);
        }

        return drew;
    }

    private static bool TryApplyFill(
        XmlNode node,
        SKPaint paint,
        float viewportWidth,
        float viewportHeight,
        Func<int, SKColor?>? resolveColor,
        Func<int, byte[]?>? resolveXmlResource,
        out SKShader? shader)
    {
        shader = null;

        if (TryGetResourceIdAttribute(node, "fillColor", out var resourceId))
        {
            var solid = resolveColor?.Invoke(resourceId) ?? TryResolveAndroidFrameworkColor(resourceId);
            if (solid is { } color && color.Alpha > 0)
            {
                paint.Color = color;
                return true;
            }

            var xml = resolveXmlResource?.Invoke(resourceId);
            if (xml is { Length: > 0 })
            {
                try
                {
                    using var stream = new MemoryStream(xml, writable: false);
                    using var axml = new AxmlFile(new StreamLoader(stream));
                    var gradient = FindGradientRoot(axml.RootNode);
                    if (gradient is not null)
                    {
                        shader = TryCreateGradientShader(gradient, viewportWidth, viewportHeight);
                        if (shader is not null)
                        {
                            paint.Shader = shader;
                            return true;
                        }
                    }
                }
                catch
                {
                    // fall through
                }
            }
        }

        var fill = ReadColorAttribute(node, "fillColor", resolveColor) ?? SKColors.White;
        if (fill.Alpha <= 0)
            return false;

        paint.Color = fill;
        return true;
    }

    private static void CollectFillResourceIds(XmlNode? node, List<int> ids)
    {
        if (node is null)
            return;

        if (TryGetResourceIdAttribute(node, "fillColor", out var id))
            ids.Add(id);

        if (node.ChildNodes is null)
            return;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
                CollectFillResourceIds(child, ids);
        }
    }

    private static XmlNode? FindGradientRoot(XmlNode? node)
    {
        if (node is null)
            return null;
        if (node.NodeName.Equals("gradient", StringComparison.OrdinalIgnoreCase))
            return node;
        if (node.ChildNodes is null)
            return null;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                var found = FindGradientRoot(child);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private static SKShader? TryCreateGradientShader(XmlNode gradient, float width, float height)
    {
        var stops = new List<(float Offset, SKColor Color)>();
        if (gradient.ChildNodes is not null)
        {
            foreach (var children in gradient.ChildNodes.Values)
            {
                foreach (var child in children)
                {
                    if (!child.NodeName.Equals("item", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var color = ReadColorAttribute(child, "color", resolveColor: null);
                    if (color is null)
                        continue;

                    var offset = ReadFloatBits(child, "offset") ?? (stops.Count == 0 ? 0f : 1f);
                    offset = Math.Clamp(offset, 0f, 1f);
                    stops.Add((offset, color.Value));
                }
            }
        }

        if (stops.Count == 0)
            return null;

        stops.Sort((a, b) => a.Offset.CompareTo(b.Offset));
        var colors = stops.Select(s => s.Color).ToArray();
        var positions = stops.Select(s => s.Offset).ToArray();

        // Android GradientDrawable.type: 0=linear, 1=radial, 2=sweep
        var typeRaw = GetAttribute(gradient, "type");
        var isRadial = typeRaw is "1" or "radial"
                       || (int.TryParse(typeRaw, out var typeInt) && typeInt == 1);

        if (isRadial)
        {
            var centerX = ReadFloatBits(gradient, "centerX") ?? width / 2f;
            var centerY = ReadFloatBits(gradient, "centerY") ?? height / 2f;
            var radius = ReadFloatBits(gradient, "gradientRadius")
                         ?? Math.Max(width, height) / 2f;
            if (radius <= 0)
                radius = Math.Max(width, height) / 2f;

            return SKShader.CreateRadialGradient(
                new SKPoint(centerX, centerY),
                radius,
                colors,
                positions,
                SKShaderTileMode.Clamp);
        }

        var startX = ReadFloatBits(gradient, "startX") ?? 0f;
        var startY = ReadFloatBits(gradient, "startY") ?? 0f;
        var endX = ReadFloatBits(gradient, "endX") ?? width;
        var endY = ReadFloatBits(gradient, "endY") ?? height;

        // Absolute pixel coords in vector viewport space (Android gradient startX/Y).
        return SKShader.CreateLinearGradient(
            new SKPoint(startX, startY),
            new SKPoint(endX, endY),
            colors,
            positions,
            SKShaderTileMode.Clamp);
    }

    private static void ApplyAlphaAttribute(XmlNode node, string name, SKPaint paint)
    {
        var alpha = ReadFloatBits(node, name);
        if (alpha is null)
            return;

        alpha = Math.Clamp(alpha.Value, 0f, 1f);
        if (paint.Shader is not null)
        {
            // Multiply shader output by alpha via color filter.
            paint.ColorFilter = SKColorFilter.CreateBlendMode(
                new SKColor(255, 255, 255, (byte)Math.Round(alpha.Value * 255)),
                SKBlendMode.DstIn);
            return;
        }

        paint.Color = paint.Color.WithAlpha((byte)Math.Round(paint.Color.Alpha * alpha.Value));
    }

    private static bool TryGetResourceIdAttribute(XmlNode node, string name, out int resourceId)
    {
        resourceId = 0;
        var raw = GetAttribute(node, name);
        if (string.IsNullOrWhiteSpace(raw))
            return false;

        if (raw.StartsWith('@'))
        {
            var hexId = raw[1..];
            if (hexId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexId = hexId[2..];
            return int.TryParse(hexId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out resourceId);
        }

        // AlphaOmega sometimes emits resource refs as raw ints (0x7F070019) — not ARGB.
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits)
            && IsLikelyResourceId(bits))
        {
            resourceId = bits;
            return true;
        }

        return false;
    }

    private static bool IsLikelyResourceId(int value)
    {
        var package = (value >> 24) & 0xFF;
        return package is 0x7F or 0x01 or 0x02;
    }

    private static XmlNode? FindVectorRoot(XmlNode? node)
    {
        if (node is null)
            return null;
        if (node.NodeName.Equals("vector", StringComparison.OrdinalIgnoreCase))
            return node;
        if (node.ChildNodes is null)
            return null;

        foreach (var children in node.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                var found = FindVectorRoot(child);
                if (found is not null)
                    return found;
            }
        }

        return null;
    }

    private static XmlNode? FindNamedChild(XmlNode? root, string name)
    {
        if (root?.ChildNodes is null)
            return null;

        foreach (var children in root.ChildNodes.Values)
        {
            foreach (var child in children)
            {
                if (child.NodeName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return child;
            }
        }

        return null;
    }

    private static string? GetAttribute(XmlNode node, string name)
    {
        if (node.Attributes is null)
            return null;

        foreach (var (key, values) in node.Attributes)
        {
            if (values is null || values.Count == 0)
                continue;

            if (key.Equals(name, StringComparison.OrdinalIgnoreCase)
                || key.EndsWith(":" + name, StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("}" + name, StringComparison.OrdinalIgnoreCase))
                return values[0];
        }

        return null;
    }

    private static float? ReadFloatAttribute(XmlNode node, string name)
    {
        var value = ReadFloatBits(node, name);
        if (value is > 0 and < 4096)
            return value;
        return null;
    }

    private static float? ReadFloatBits(XmlNode node, string name)
    {
        var raw = GetAttribute(node, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        // AlphaOmega surfaces android:float / dimension attrs as raw int bits ("1119833578").
        // Prefer bit-cast for integer-looking tokens — float.TryParse would treat them as huge decimals.
        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bits))
        {
            var fromBits = BitConverter.Int32BitsToSingle(bits);
            if (float.IsFinite(fromBits))
                return fromBits;
        }

        if (float.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var f)
            && float.IsFinite(f))
            return f;

        return null;
    }

    private static SKColor? ReadColorAttribute(XmlNode node, string name, Func<int, SKColor?>? resolveColor)
    {
        var raw = GetAttribute(node, name);
        if (string.IsNullOrWhiteSpace(raw))
            return null;

        if (raw.StartsWith('@'))
        {
            var hexId = raw[1..];
            if (hexId.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                hexId = hexId[2..];
            if (int.TryParse(hexId, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var resourceId))
            {
                return resolveColor?.Invoke(resourceId)
                       ?? TryResolveAndroidFrameworkColor(resourceId);
            }

            return null;
        }

        if (raw.StartsWith('#')
            && uint.TryParse(raw.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var hex))
            return new SKColor(hex);

        if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var argb))
        {
            // Resource refs sometimes appear as raw ints — do not treat as ARGB.
            if (IsLikelyResourceId(argb))
            {
                return resolveColor?.Invoke(argb)
                       ?? TryResolveAndroidFrameworkColor(argb);
            }

            return new SKColor(unchecked((uint)argb));
        }

        return null;
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex PathWhitespace();
}
