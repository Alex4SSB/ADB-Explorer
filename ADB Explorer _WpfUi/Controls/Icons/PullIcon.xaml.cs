namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for PushIcon.xaml
/// </summary>
public partial class PullIcon : UserControl
{
    public PullIcon(int spacing = -4)
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
          typeof(PullIcon), new PropertyMetadata(default(int)));
}
