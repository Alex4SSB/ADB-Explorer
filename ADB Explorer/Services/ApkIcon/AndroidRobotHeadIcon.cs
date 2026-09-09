using SkiaSharp;

namespace ADB_Explorer.Services;

/// <summary>
/// Android "bugdroid" head silhouette (no body/antennae shadow, just the head), used as the small
/// overlay badge composited onto the root drive icon (<see cref="Helpers.FileToIconConverter.ComposeRootDriveIcon"/>),
/// mirroring how Windows paints its logo over the C: drive icon. Sourced from the official Android
/// brand assets as an SVG path (originally <c>Assets/Android_robot_head.svg</c>) and kept as code so
/// it renders sharp at any requested pixel size instead of being scaled from a stored raster/vector asset.
/// </summary>
internal static class AndroidRobotHeadIcon
{
    private const float ViewboxWidth = 918.6f;
    private const float ViewboxHeight = 515.1f;

    /// <summary>Width-to-height ratio of the source artwork; use it to size a badge without distorting it.</summary>
    public const float AspectRatio = ViewboxWidth / ViewboxHeight;

    private const string PathData =
        "M918.6 515.1h-918.6c14.7-155.7 103.7-288.7 235.1-359.9l-76.2-132c-4.3-7.4-1.8-16.8 5.6-21.1s16.8-1.8 21.1 5.6l77.2 133.7c58.9-26.9 125.2-41.9 196.5-41.9s137.6 15 196.5 41.9l77.2-133.7c4.2-7.4 13.7-9.9 21-5.6s9.9 13.7 5.6 21.1l-76.2 132c131.5 71.2 220.5 204.2 235.2 359.9zm-248.5-129c21.3 0 38.6-17.3 38.5-38.5 0-21.2-17.2-38.5-38.5-38.5-21.2 0-38.5 17.2-38.5 38.5 0 21.2 17.2 38.5 38.5 38.5zm-421.7 0c21.3 0 38.6-17.3 38.5-38.5 0-21.2-17.2-38.5-38.5-38.5-21.2 0-38.5 17.2-38.5 38.5 0 21.2 17.2 38.5 38.5 38.5z";

    // The two eye circles from PathData, reconstructed with absolute coordinates so they can be painted in instead of left as holes.
    private const string EyePathData =
        "M670.1 386.1c21.3 0 38.6-17.3 38.5-38.5 0-21.2-17.2-38.5-38.5-38.5-21.2 0-38.5 17.2-38.5 38.5 0 21.2 17.2 38.5 38.5 38.5zM248.4 386.1c21.3 0 38.6-17.3 38.5-38.5 0-21.2-17.2-38.5-38.5-38.5-21.2 0-38.5 17.2-38.5 38.5 0 21.2 17.2 38.5 38.5 38.5z";

    private static readonly SKColor FillColor = new(0x3D, 0xDC, 0x84);
    private static readonly SKColor EyeColor = new(0x40, 0x40, 0x40);

    /// <summary>
    /// Renders the head at <paramref name="width"/>×<paramref name="height"/> pixels. Pass a
    /// <paramref name="height"/> derived from <see cref="AspectRatio"/> to avoid distorting it.
    /// </summary>
    public static BitmapSource Render(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        using var bitmap = new SKBitmap(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var canvas = new SKCanvas(bitmap);
        canvas.Clear(SKColors.Transparent);

        using var path = SKPath.ParseSvgPathData(PathData);
        path.FillType = SKPathFillType.Winding;

        canvas.Scale(width / ViewboxWidth, height / ViewboxHeight);

        using var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Fill,
            Color = FillColor,
        };
        canvas.DrawPath(path, paint);

        using var eyePath = SKPath.ParseSvgPathData(EyePathData);
        paint.Color = EyeColor;
        canvas.DrawPath(eyePath, paint);

        return ApkVectorIconRenderer.ToBitmapSource(bitmap);
    }
}
