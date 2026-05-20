namespace ADB_Explorer.Controls;

public partial class LargeThumbsIcon : UserControl
{
    public LargeThumbsIcon()
    {
        InitializeComponent();
    }

    public int SubFontSize
    {
        get => (int)GetValue(SubFontSizeProperty);
        set => SetValue(SubFontSizeProperty, value);
    }

    public static readonly DependencyProperty SubFontSizeProperty =
        DependencyProperty.Register("SubFontSize", typeof(int),
          typeof(LargeThumbsIcon), new PropertyMetadata(7));
}
