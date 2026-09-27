namespace ADB_Explorer.Controls;

/// <summary>
/// The battery and Android version of the device the explorer pane in <see cref="Instance"/> is
/// browsing - the status bar's device readout, for a split view whose panes are on different devices.
/// </summary>
public partial class PaneDeviceInfo : UserControl
{
    public static readonly DependencyProperty InstanceProperty =
        DependencyProperty.Register(
            nameof(Instance),
            typeof(ExplorerInstance),
            typeof(PaneDeviceInfo),
            new PropertyMetadata(null, (d, e) => ((PaneDeviceInfo)d)._info.Instance = (ExplorerInstance?)e.NewValue));

    public ExplorerInstance? Instance
    {
        get => (ExplorerInstance?)GetValue(InstanceProperty);
        set => SetValue(InstanceProperty, value);
    }

    private readonly PaneDeviceInfoViewModel _info = new();

    public PaneDeviceInfo()
    {
        InitializeComponent();

        DataContext = _info;

        Loaded += (_, _) => _info.Attach();
        Unloaded += (_, _) => _info.Detach();
    }
}
