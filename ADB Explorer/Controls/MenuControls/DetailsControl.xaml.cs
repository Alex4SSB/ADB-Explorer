using ADB_Explorer.Models;

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

    public DetailsPane.SidePaneMode Mode
    {
        get => (DetailsPane.SidePaneMode)GetValue(ModeProperty);
        set => SetValue(ModeProperty, value);
    }

    public static readonly DependencyProperty ModeProperty =
        DependencyProperty.Register(nameof(Mode), typeof(DetailsPane.SidePaneMode),
          typeof(DetailsControl), new PropertyMetadata(DetailsPane.SidePaneMode.Details));

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
            Mode = DetailsPane.IsPreviewAllowed()
                ? Data.Settings.SidePane
                : DetailsPane.SidePaneMode.Details;
        };
    }
}
