namespace ADB_Explorer.Controls;

/// <summary>
/// Interaction logic for TransferIndicator.xaml
/// </summary>
public partial class TransferIndicator : UserControl
{
    public TransferIndicator()
    {
        InitializeComponent();
    }

    public bool IsUpVisible
    {
        get => (bool)GetValue(IsUpVisibleProperty);
        set => SetValue(IsUpVisibleProperty, value);
    }

    public static readonly DependencyProperty IsUpVisibleProperty =
        DependencyProperty.Register("IsUpVisible", typeof(bool),
          typeof(TransferIndicator), new PropertyMetadata(null));

    public bool IsDownVisible
    {
        get => (bool)GetValue(IsDownVisibleProperty);
        set => SetValue(IsDownVisibleProperty, value);
    }

    public static readonly DependencyProperty IsDownVisibleProperty =
        DependencyProperty.Register("IsDownVisible", typeof(bool),
          typeof(TransferIndicator), new PropertyMetadata(null));

    public Visibility UpVisibility => IsUpVisible ? Visibility.Visible : Visibility.Collapsed;

    public Visibility DownVisibility => IsDownVisible ? Visibility.Visible : Visibility.Collapsed;
}
