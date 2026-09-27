namespace ADB_Explorer.ViewModels.Pages;

/// <summary>
/// The device readout of one explorer pane: its device's Android version and battery. Unlike the
/// page's own, which follows the focused pane, this reads the pane in <see cref="Instance"/>.
/// </summary>
public partial class PaneDeviceInfoViewModel : ObservableObject
{
    private ExplorerInstance? _instance;
    private Battery? _subscribedBattery;
    private bool _attached;

    [ObservableProperty]
    public partial LogicalDeviceViewModel? Device { get; set; }

    [ObservableProperty]
    public partial Battery? Battery { get; set; }

    [ObservableProperty]
    public partial bool IsBatteryVisible { get; set; }

    public ExplorerInstance? Instance
    {
        get => _instance;
        set
        {
            if (ReferenceEquals(_instance, value))
                return;

            if (_attached)
                Unsubscribe();

            _instance = value;

            if (_attached)
                Subscribe();
        }
    }

    /// <summary>Starts following the pane; done while the readout is on screen, so a closed pane's isn't kept alive.</summary>
    public void Attach()
    {
        if (_attached)
            return;

        _attached = true;
        Subscribe();
    }

    public void Detach()
    {
        if (!_attached)
            return;

        Unsubscribe();
        _attached = false;
    }

    private void Subscribe()
    {
        _instance?.PropertyChanged += Instance_PropertyChanged;
        Data.Settings.PropertyChanged += Settings_PropertyChanged;

        Refresh();
    }

    private void Unsubscribe()
    {
        _instance?.PropertyChanged -= Instance_PropertyChanged;
        Data.Settings.PropertyChanged -= Settings_PropertyChanged;

        _subscribedBattery?.PropertyChanged -= Battery_PropertyChanged;
        _subscribedBattery = null;
    }

    private void Instance_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ExplorerInstance.EffectiveDevice))
            QueueRefresh();
    }

    private void Settings_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AppSettings.PollBattery))
            QueueRefresh();
    }

    private void Battery_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(Models.Battery.ChargeState) or nameof(Models.Battery.Level))
            QueueRefresh();
    }

    private void QueueRefresh() => App.SafeBeginInvoke(Refresh, DispatcherPriority.Background);

    private void Refresh()
    {
        var device = _instance?.EffectiveDevice;
        var battery = device?.Battery;

        if (!ReferenceEquals(_subscribedBattery, battery))
        {
            _subscribedBattery?.PropertyChanged -= Battery_PropertyChanged;
            _subscribedBattery = battery;
            _subscribedBattery?.PropertyChanged += Battery_PropertyChanged;
        }

        Device = device;
        Battery = battery;

        IsBatteryVisible = Data.Settings.PollBattery
            && battery?.ChargeState is not Models.Battery.ChargingState.Unknown
            && battery?.Level is not null;
    }
}
