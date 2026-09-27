using Wpf.Ui.Controls;

namespace ADB_Explorer.Controls;

public enum RtlBehavior
{
    None,
    /// <summary>
    /// Forces the icon to LTR to prevent it from flipping horizontally when UI culture is RTL.
    /// </summary>
    ForceLtr,
    /// <summary>
    /// Flips the icon horizontally when UI Culture is RTL.
    /// </summary>
    FlipInRtl,
}

public class BaseIcon
{
    private const string DefaultForegroundBrush = "TextFillColorPrimaryBrush";

    private readonly Func<object> _create;
    private object? _iconContent;

    /// <summary>Created on first read, so an icon can be declared off the UI thread (e.g. in static state).</summary>
    public object IconContent => _iconContent ??= _create();

    public double Size { get; }

    public BaseIcon(string glyph, double fontSize = 22, string? brush = null, RtlBehavior rtlBehavior = RtlBehavior.None)
    {
        Size = fontSize;
        _create = () =>
        {
            FontIcon fontIcon = new()
            {
                Glyph = glyph,
                FontSize = fontSize,
                Style = CreateForegroundStyle(typeof(FontIcon), "BaseIconFontStyle", brush ?? DefaultForegroundBrush),
            };

            ApplyRtlBehavior(fontIcon, rtlBehavior);
            return fontIcon;
        };
    }

    public BaseIcon(Geometry data, double height = 22, string? brush = null, Stretch stretch = Stretch.Uniform, RtlBehavior rtlBehavior = RtlBehavior.None)
    {
        Size = height;
        _create = () =>
        {
            FluentPathIcon icon = new()
            {
                Data = data,
                Stretch = stretch,
                Width = height,
                Height = height,
                Style = CreateForegroundStyle(typeof(FluentPathIcon), "BaseIconPathStyle", brush ?? DefaultForegroundBrush),
            };

            ApplyRtlBehavior(icon, rtlBehavior);
            return icon;
        };
    }

    public BaseIcon(BitmapSource bitmap, double height = 22)
    {
        Size = height;
        _create = () =>
        {
            // ImageIcon (not a plain Image) so this also satisfies controls whose Icon is typed
            // IconElement (e.g. ui:CardAction) - a bare Image fails CardAction.Icon's coercion.
            return new ImageIcon()
            {
                Source = bitmap,
                Width = height,
                Height = height,
            };
        };
    }

    // For non-square vector logos (DrawingImage) only ever shown via a plain ContentPresenter, never
    // an IconElement-coerced Icon property: ImageIcon hardcodes Stretch.UniformToFill, which would
    // crop them to a square instead of preserving their own aspect ratio like Stretch.Uniform does.
    public BaseIcon(DrawingImage drawingImage, double height = 22, Stretch stretch = Stretch.Uniform)
    {
        Size = height;
        _create = () =>
        {
            return new System.Windows.Controls.Image()
            {
                Source = drawingImage,
                Height = height,
                Stretch = stretch,
            };
        };
    }

    // Same as the DrawingImage overload, for a non-square raster BitmapSource shown via a plain
    // ContentPresenter. stretch has no default so this stays unambiguous with the square BitmapSource
    // overload above - callers must opt in explicitly.
    public BaseIcon(BitmapSource bitmap, double height, Stretch stretch)
    {
        Size = height;
        _create = () =>
        {
            return new System.Windows.Controls.Image()
            {
                Source = bitmap,
                Height = height,
                Stretch = stretch,
            };
        };
    }

    /// <summary>Wraps an element that sizes itself, so nothing forces its width or height.</summary>
    public BaseIcon(Func<UIElement> createContent, double size)
    {
        Size = size;
        _create = () => createContent();
    }

    public BaseIcon(UserControl content, double size = 18, RtlBehavior rtlBehavior = RtlBehavior.None)
        : this(() => content, size, rtlBehavior)
    { }

    public BaseIcon(Func<UserControl> createContent, double size = 18, RtlBehavior rtlBehavior = RtlBehavior.None)
    {
        Size = size;
        _create = () =>
        {
            var content = createContent();
            if (content is ScaledPathIcon scaledPathIcon)
                scaledPathIcon.Size = size;

            content.Width = size;
            content.Height = size;

            ApplyRtlBehavior(content, rtlBehavior);
            return content;
        };
    }

    private static void ApplyRtlBehavior(FrameworkElement element, RtlBehavior behavior)
    {
        if (behavior is RtlBehavior.None)
            return;

        element.FlowDirection = FlowDirection.LeftToRight;

        if (behavior is RtlBehavior.FlipInRtl && Data.RuntimeSettings.IsRTL)
            element.LayoutTransform = new ScaleTransform(-1, 1);
    }

    private static Style CreateForegroundStyle(Type targetType, string baseStyleKey, string enabledBrushKey)
    {
        var baseStyle = (Style)App.Current.Resources[baseStyleKey];
        if (enabledBrushKey == DefaultForegroundBrush)
            return baseStyle;

        var style = new Style(targetType, baseStyle);
        style.Setters.Add(new Setter(Control.ForegroundProperty, new DynamicResourceExtension(enabledBrushKey)));
        return style;
    }
}
