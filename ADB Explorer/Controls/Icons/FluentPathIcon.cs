using Wpf.Ui.Controls;

namespace ADB_Explorer.Controls;

/// <summary>
/// Path-based icon for use with Wpf.Ui controls that require <see cref="IconElement"/> (e.g. NavigationViewItem).
/// </summary>
public class FluentPathIcon : IconElement
{
    /// <summary>Matches Wpf.Ui <c>DefaultIconFontSize</c> so path icons align with <see cref="FontIcon"/>.</summary>
    private const double DefaultSize = 16;

    static FluentPathIcon()
    {
        WidthProperty.OverrideMetadata(typeof(FluentPathIcon), new FrameworkPropertyMetadata(DefaultSize));
        HeightProperty.OverrideMetadata(typeof(FluentPathIcon), new FrameworkPropertyMetadata(DefaultSize));
    }

    public static readonly DependencyProperty DataProperty = DependencyProperty.Register(
        nameof(Data),
        typeof(Geometry),
        typeof(FluentPathIcon),
        new PropertyMetadata(Geometry.Empty, OnDataChanged));

    public static readonly DependencyProperty StretchProperty = DependencyProperty.Register(
        nameof(Stretch),
        typeof(Stretch),
        typeof(FluentPathIcon),
        new PropertyMetadata(Stretch.Uniform, OnStretchChanged));

    public Geometry Data
    {
        get => (Geometry)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    protected System.Windows.Shapes.Path? PathElement { get; private set; }

    protected override UIElement InitializeChildren()
    {
        PathElement = new System.Windows.Shapes.Path
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Stretch = Stretch,
            Data = Data,
        };

        PathElement.SetBinding(System.Windows.Shapes.Shape.FillProperty, new Binding(nameof(Foreground)) { Source = this });
        PathElement.SetBinding(FrameworkElement.WidthProperty, new Binding(nameof(Width)) { Source = this });
        PathElement.SetBinding(FrameworkElement.HeightProperty, new Binding(nameof(Height)) { Source = this });

        // Transparent host so the whole icon rectangle is hit-testable (tooltips, clicks).
        var host = new Grid { Background = Brushes.Transparent };
        host.Children.Add(PathElement);
        return host;
    }

    protected override Size MeasureOverride(Size availableSize) => GetLayoutSize();

    protected override Size ArrangeOverride(Size finalSize) => base.ArrangeOverride(GetLayoutSize());

    private Size GetLayoutSize() => new(
        double.IsNaN(Width) ? DefaultSize : Width,
        double.IsNaN(Height) ? DefaultSize : Height);

    private static void OnDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (FluentPathIcon)d;
        if (self.PathElement is null)
            return;

        self.PathElement.Data = (Geometry)e.NewValue;
    }

    private static void OnStretchChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (FluentPathIcon)d;
        if (self.PathElement is null)
            return;

        self.PathElement.Stretch = (Stretch)e.NewValue;
    }
}
