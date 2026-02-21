namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for PushIcon.xaml
/// </summary>
public partial class PushIcon : UserControl
{
    public PushIcon(int spacing = -4)
    {
        InitializeComponent();

        Spacing = spacing;
    }

    public int Spacing
    {
        get => (int)GetValue(SpacingProperty);
        set => SetValue(SpacingProperty, value);
    }

    public static readonly DependencyProperty SpacingProperty =
        DependencyProperty.Register("Spacing", typeof(int),
          typeof(PushIcon), new PropertyMetadata(default(int)));
}
