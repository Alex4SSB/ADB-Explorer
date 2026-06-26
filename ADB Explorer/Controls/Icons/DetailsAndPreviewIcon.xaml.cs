namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsAndPreviewIcon.xaml
/// </summary>
public partial class DetailsAndPreviewIcon : UserControl
{
    public DetailsPane.SidePaneMode Mode
    {
        get => (DetailsPane.SidePaneMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(DetailsPane.SidePaneMode),
          typeof(DetailsAndPreviewIcon), new PropertyMetadata(DetailsPane.SidePaneMode.Details));

    public double Size
    {
        get => (double)GetValue(SizeProperty);
        set => SetValue(SizeProperty, value);
    }

    public static readonly DependencyProperty SizeProperty =
        DependencyProperty.Register(nameof(Size), typeof(double),
          typeof(DetailsAndPreviewIcon), new PropertyMetadata(24.0d));

    public Stretch Stretch
    {
        get => (Stretch)GetValue(StretchProperty);
        set => SetValue(StretchProperty, value);
    }

    public static readonly DependencyProperty StretchProperty =
        DependencyProperty.Register(nameof(Stretch), typeof(Stretch),
          typeof(DetailsAndPreviewIcon), new PropertyMetadata(Stretch.None));

    public DetailsAndPreviewIcon()
    {
        InitializeComponent();
    }
}
