namespace ADB_Explorer.Controls;

/// <summary>
/// Multi-path Fluent icon scaled to <see cref="Size"/> via a shared coordinate space.
/// </summary>
public class ScaledPathIcon : UserControl
{
    public ScaledPathIcon()
    {
        // Transparent fill so the whole artboard is a tooltip / hit-test target.
        Background = Brushes.Transparent;
    }

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static readonly DependencyProperty SizeProperty = DependencyProperty.Register(
        nameof(Size),
        typeof(double),
        typeof(ScaledPathIcon),
        new PropertyMetadata(16.0d));

    /// <summary>How the icon reacts to an RTL UI culture, applied by <see cref="BaseIcon"/>.</summary>
    public RtlBehavior RtlBehavior
    {
        get => (RtlBehavior)GetValue(RtlBehaviorProperty);
        set => SetValue(RtlBehaviorProperty, value);
    }

    public static readonly DependencyProperty RtlBehaviorProperty = DependencyProperty.Register(
        nameof(RtlBehavior),
        typeof(RtlBehavior),
        typeof(ScaledPathIcon),
        new PropertyMetadata(RtlBehavior.None));

    /// <summary>Fluent SVG viewBox dimension (16, 20, or 24).</summary>
    public double ArtboardSize
    {
        get => (double)GetValue(ArtboardSizeProperty);
        set => SetValue(ArtboardSizeProperty, value);
    }

    public static readonly DependencyProperty ArtboardSizeProperty = DependencyProperty.Register(
        nameof(ArtboardSize),
        typeof(double),
        typeof(ScaledPathIcon),
        new PropertyMetadata(16.0d));
}
