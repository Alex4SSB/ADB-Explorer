using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Rendering;
using Wpf.Ui.Appearance;

namespace ADB_Explorer.Helpers;

/// <summary>
/// AvalonEdit built-in highlighters target a light background. In dark mode, remap
/// foreground/background colors (HSL lightness invert + mild desaturation), matching
/// the approach used by ILSpy's <c>ThemeManager.GetColorForDarkTheme</c>.
/// </summary>
internal static class AvalonEditHighlightingTheme
{
    public static HighlightingColor GetColorForDarkTheme(HighlightingColor lightColor)
    {
        if (lightColor.Foreground is null && lightColor.Background is null)
            return lightColor;

        var darkColor = lightColor.Clone();
        darkColor.Foreground = AdjustBrush(darkColor.Foreground);
        darkColor.Background = AdjustBrush(darkColor.Background);
        return darkColor;
    }

    private static HighlightingBrush? AdjustBrush(HighlightingBrush? lightBrush)
    {
        if (lightBrush is SimpleHighlightingBrush simple
            && simple.GetBrush(null) is SolidColorBrush { Color: var color })
        {
            return new SimpleHighlightingBrush(AdjustColor(color));
        }

        return lightBrush;
    }

    private static Color AdjustColor(Color color)
    {
        var drawing = System.Drawing.Color.FromArgb(color.R, color.G, color.B);
        var h = drawing.GetHue();
        var s = drawing.GetSaturation();
        var l = drawing.GetBrightness();

        // Invert lightness, slightly biased brighter for dark backgrounds.
        l = 1f - MathF.Pow(l, 1.2f);

        // Desaturate intense colors that become neon on dark.
        if (s > 0.75f && l < 0.75f)
        {
            s *= 0.75f;
            l *= 1.2f;
        }

        var (r, g, b) = HslToRgb(h, s, l);
        return Color.FromArgb(color.A, r, g, b);
    }

    private static (byte r, byte g, byte b) HslToRgb(float h, float s, float l)
    {
        var c = (1f - Math.Abs(2f * l - 1f)) * s;
        h = h % 360f / 60f;
        var x = c * (1f - Math.Abs(h % 2f - 1f));

        var (r1, g1, b1) = (int)Math.Floor(h) switch
        {
            0 => (c, x, 0f),
            1 => (x, c, 0f),
            2 => (0f, c, x),
            3 => (0f, x, c),
            4 => (x, 0f, c),
            _ => (c, 0f, x),
        };

        var m = l - c / 2f;
        return (
            (byte)Math.Clamp((r1 + m) * 255f, 0, 255),
            (byte)Math.Clamp((g1 + m) * 255f, 0, 255),
            (byte)Math.Clamp((b1 + m) * 255f, 0, 255));
    }
}

/// <summary>
/// <see cref="TextEditor"/> subclass so AvalonEdit installs our colorizer instead of the default.
/// </summary>
public class ThemeAwareTextEditor : TextEditor
{
    protected override IVisualLineTransformer CreateColorizer(IHighlightingDefinition highlightingDefinition)
        => new ThemeAwareHighlightingColorizer(highlightingDefinition);
}

internal sealed class ThemeAwareHighlightingColorizer(IHighlightingDefinition definition)
    : HighlightingColorizer(definition)
{
    private readonly Dictionary<HighlightingColor, HighlightingColor> _darkColors = new();

    protected override void ApplyColorToElement(VisualLineElement element, HighlightingColor color)
    {
        if (ApplicationThemeManager.GetAppTheme() is ApplicationTheme.Dark)
            color = GetDarkColor(color);

        base.ApplyColorToElement(element, color);
    }

    private HighlightingColor GetDarkColor(HighlightingColor lightColor)
    {
        if (!_darkColors.TryGetValue(lightColor, out var darkColor))
        {
            darkColor = AvalonEditHighlightingTheme.GetColorForDarkTheme(lightColor);
            _darkColors[lightColor] = darkColor;
        }

        return darkColor;
    }
}
