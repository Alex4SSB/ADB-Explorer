using SkiaSharp;

namespace ADB_Explorer.Services;

/// <summary>
/// Large-icon placeholder for packages without a fetched launcher icon.
/// Geometry is the Android Studio / adaptive-icon template
/// (<c>ic_launcher_background</c> + <c>ic_launcher_foreground</c>), sourced from
/// <see href="https://github.com/android/adaptive-apps-samples">android/adaptive-apps-samples</see>
/// (Apache-2.0).
/// </summary>
internal static class DefaultAndroidPackageIcon
{
    private const float LayerSize = 108f;

    /// <summary>
    /// Adaptive-icon masked viewport (center of the 108dp layer). Matching
    /// <c>AdaptiveIconDrawable</c> so the Bugdroid/grid match launcher-sized icons.
    /// </summary>
    private const float MaskedViewport = 72f;

    private const float LayerInset = (LayerSize - MaskedViewport) / 2f; // 18
    private const int DefaultSize = 512;

    /// <summary>Match neighboring package icons that include adaptive safe-zone padding.</summary>
    private const float ContentScale = 0.8f;

    /// <summary>Rounded-square mask; ~8% matches typical launcher icons better than a squircle.</summary>
    private const float CornerRadiusFraction = 0.08f;

    // Foreground long-shadow path (gradient fill).
    private const string ShadowPathData =
        "M31,63.928c0,0 6.4,-11 12.1,-13.1c7.2,-2.6 26,-1.4 26,-1.4l38.1,38.1L107,108.928l-32,-1L31,63.928z";

    // White Bugdroid head (antennae + eyes).
    private const string HeadPathData =
        "M65.3,45.828l3.8,-6.6c0.2,-0.4 0.1,-0.9 -0.3,-1.1c-0.4,-0.2 -0.9,-0.1 -1.1,0.3l-3.9,6.7c-6.3,-2.8 -13.4,-2.8 -19.7,0l-3.9,-6.7c-0.2,-0.4 -0.7,-0.5 -1.1,-0.3C38.8,38.328 38.7,38.828 38.9,39.228l3.8,6.6C36.2,49.428 31.7,56.028 31,63.928h46C76.3,56.028 71.8,49.428 65.3,45.828zM43.4,57.328c-0.8,0 -1.5,-0.5 -1.8,-1.2c-0.3,-0.7 -0.1,-1.5 0.4,-2.1c0.5,-0.5 1.4,-0.7 2.1,-0.4c0.7,0.3 1.2,1 1.2,1.8C45.3,56.528 44.5,57.328 43.4,57.328L43.4,57.328zM64.6,57.328c-0.8,0 -1.5,-0.5 -1.8,-1.2s-0.1,-1.5 0.4,-2.1c0.5,-0.5 1.4,-0.7 2.1,-0.4c0.7,0.3 1.2,1 1.2,1.8C66.5,56.528 65.6,57.328 64.6,57.328L64.6,57.328z";

    private static readonly Lazy<BitmapSource> LazyBitmap =
        new(() => Render(DefaultSize), LazyThreadSafetyMode.ExecutionAndPublication);

    private static readonly Lazy<BitmapSource> LazyGrayscaleBitmap =
        new(CreateGrayscaleBitmap, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Cached 512×512 adaptive-template icon for UI bindings.</summary>
    public static BitmapSource Bitmap => LazyBitmap.Value;

    /// <summary>Desaturated copy of <see cref="Bitmap"/> — package icon-view placeholder while loading.</summary>
    public static BitmapSource GrayscaleBitmap => LazyGrayscaleBitmap.Value;

    public static BitmapSource Render(int size = DefaultSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        using var sk = RenderToSkBitmap(size);
        return ApkVectorIconRenderer.ToBitmapSource(sk);
    }

    private static BitmapSource CreateGrayscaleBitmap()
    {
        using var color = RenderToSkBitmap(DefaultSize);
        using var gray = new SKBitmap(color.Width, color.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(gray);
        canvas.Clear(SKColors.Transparent);

        // Rec. 709 luminance → RGB (keep alpha).
        float[] matrix =
        [
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0.2126f, 0.7152f, 0.0722f, 0, 0,
            0, 0, 0, 1, 0,
        ];
        using var paint = new SKPaint
        {
            IsAntialias = true,
            ColorFilter = SKColorFilter.CreateColorMatrix(matrix),
        };
        canvas.DrawBitmap(color, 0, 0, paint);
        return ApkVectorIconRenderer.ToBitmapSource(gray);
    }

    public static SKBitmap RenderToSkBitmap(int size = DefaultSize)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(size, 1);

        var bitmap = new SKBitmap(size, size, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        var contentSize = size * ContentScale;
        var pad = (size - contentSize) / 2f;
        var radius = contentSize * CornerRadiusFraction;

        canvas.Save();
        using (var clip = new SKRoundRect(new SKRect(pad, pad, pad + contentSize, pad + contentSize), radius))
            canvas.ClipRoundRect(clip, antialias: true);

        // Map the center 72×72 of the 108×108 layers into the padded content rect.
        canvas.Translate(pad, pad);
        canvas.Scale(contentSize / MaskedViewport);
        canvas.Translate(-LayerInset, -LayerInset);

        DrawBackground(canvas);
        DrawForeground(canvas);
        canvas.Restore();

        return bitmap;
    }

    private static void DrawBackground(SKCanvas canvas)
    {
        using var fill = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = new SKColor(0x3D, 0xDC, 0x84),
        };
        canvas.DrawRect(0, 0, LayerSize, LayerSize, fill);

        // SkiaSharp SKColor is (r, g, b, a). Template stroke is #33FFFFFF.
        using var grid = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            Color = new SKColor(255, 255, 255, 0x33),
            StrokeWidth = 0.8f,
            StrokeCap = SKStrokeCap.Butt,
            BlendMode = SKBlendMode.SrcOver,
        };

        // Full-span grid + inner segments from ic_launcher_background.xml.
        for (var i = 9; i <= 99; i += 10)
        {
            canvas.DrawLine(i, 0, i, LayerSize, grid);
            canvas.DrawLine(0, i, LayerSize, i, grid);
        }

        for (var y = 29; y <= 79; y += 10)
            canvas.DrawLine(19, y, 89, y, grid);
        for (var x = 29; x <= 79; x += 10)
            canvas.DrawLine(x, 19, x, 89, grid);
    }

    private static void DrawForeground(SKCanvas canvas)
    {
        using var shadowPath = SKPath.ParseSvgPathData(ShadowPathData);
        if (shadowPath is not null && !shadowPath.IsEmpty)
        {
            // Template gradient: #44000000 → #00000000 (r,g,b,a).
            using var shader = SKShader.CreateLinearGradient(
                new SKPoint(42.9492f, 49.59793f),
                new SKPoint(85.84757f, 92.4963f),
                [new SKColor(0, 0, 0, 0x44), new SKColor(0, 0, 0, 0)],
                [0f, 1f],
                SKShaderTileMode.Clamp);
            using var shadowPaint = new SKPaint
            {
                IsAntialias = true,
                Style = SKPaintStyle.Fill,
                Shader = shader,
                BlendMode = SKBlendMode.SrcOver,
            };
            canvas.DrawPath(shadowPath, shadowPaint);
        }

        using var headPath = SKPath.ParseSvgPathData(HeadPathData);
        if (headPath is null || headPath.IsEmpty)
            return;

        headPath.FillType = SKPathFillType.Winding;
        using var headPaint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = SKColors.White,
            BlendMode = SKBlendMode.SrcOver,
        };
        canvas.DrawPath(headPath, headPaint);
    }
}
