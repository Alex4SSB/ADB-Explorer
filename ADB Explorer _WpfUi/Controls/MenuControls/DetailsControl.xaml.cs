namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for DetailsControl.xaml
/// </summary>
public partial class DetailsControl : UserControl
{

    public bool IsChecked
    {
        get => (bool)GetValue(IsCheckedProperty);
        set => SetValue(IsCheckedProperty, value);
    }

    public static readonly DependencyProperty IsCheckedProperty =
        DependencyProperty.Register("IsChecked", typeof(bool),
          typeof(DetailsControl), new PropertyMetadata(false));

    public DetailsControl()
    {
        InitializeComponent();
    }
}
