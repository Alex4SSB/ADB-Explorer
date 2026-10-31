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
        DependencyProperty.Register(nameof(IsChecked), typeof(bool),
          typeof(DetailsControl), new PropertyMetadata(false));

    public SidePaneMode Mode
    {
        get => (SidePaneMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(SidePaneMode),
          typeof(DetailsControl), new PropertyMetadata(SidePaneMode.Details));

    public bool IsModeSwitchAllowed
    {
        get => (bool)GetValue(IsModeSwitchAllowedProperty);
        set => SetValue(IsModeSwitchAllowedProperty, value);
    }

    public static readonly DependencyProperty IsModeSwitchAllowedProperty =
        DependencyProperty.Register(nameof(IsModeSwitchAllowed), typeof(bool),
          typeof(DetailsControl), new PropertyMetadata(false));

    public Action RequestModeRefresh
    {
        get => (Action)GetValue(RequestFileRefreshProperty);
        set => SetValue(RequestFileRefreshProperty, value);
    }

    public static readonly DependencyProperty RequestFileRefreshProperty =
        DependencyProperty.Register(nameof(RequestModeRefresh), typeof(Action),
          typeof(DetailsControl), new PropertyMetadata(null));

    public DetailsControl()
    {
        InitializeComponent();

        RequestModeRefresh = () =>
        {
            IsModeSwitchAllowed = Data.FileActions.IsPreviewAllowed;
            Mode = IsModeSwitchAllowed
                ? Data.Settings.SidePane
                : SidePaneMode.Details;
        };
    }

    private ToolTip? _handedOffToolTip;

    // WPF keeps a tooltip open while the mouse moves toward it, which covers the other half,
    // and won't open the other half's tooltip after we close it - so hand it over ourselves
    private void SplitHalf_MouseEnter(object sender, MouseEventArgs e)
    {
        var half = (FrameworkElement)sender;
        var other = half == ModeButton ? PaneButton : ModeButton;
        if (other.ToolTip is not ToolTip { IsOpen: true } otherToolTip)
            return;

        otherToolTip.IsOpen = false;

        if (half.ToolTip is ToolTip toolTip)
        {
            toolTip.PlacementTarget = half;
            toolTip.IsOpen = true;
            _handedOffToolTip = toolTip;
        }
    }

    private void SplitPanel_MouseLeave(object sender, MouseEventArgs e)
    {
        if (_handedOffToolTip is null)
            return;

        _handedOffToolTip.IsOpen = false;
        _handedOffToolTip = null;
    }

    private void PaneButton_Click(object sender, RoutedEventArgs e) => IsChecked = !IsChecked;

    private void ModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!Data.FileActions.IsPreviewAllowed)
            return;

        Data.Settings.SidePane = Mode is SidePaneMode.Details
            ? SidePaneMode.Preview
            : SidePaneMode.Details;
    }
}
