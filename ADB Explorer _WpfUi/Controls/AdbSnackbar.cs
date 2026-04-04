using Wpf.Ui.Controls;

namespace ADB_Explorer.Controls;

public enum SnackProgressMode
{
    Off,
    Indeterminate,
    TimeOutDisplay,
    ExternalProgress,
}

public class AdbSnackbar : Snackbar
{
    private DispatcherTimer _countdownTimer;

    public AdbSnackbar(SnackbarPresenter presenter) : base(presenter) { }

    public static readonly DependencyProperty ProgressModeProperty =
        DependencyProperty.Register(
            nameof(ProgressMode),
            typeof(SnackProgressMode),
            typeof(AdbSnackbar),
            new PropertyMetadata(SnackProgressMode.Off));

    public SnackProgressMode ProgressMode
    {
        get => (SnackProgressMode)GetValue(ProgressModeProperty);
        set => SetValue(ProgressModeProperty, value);
    }

    public static readonly DependencyProperty ProgressValueProperty =
        DependencyProperty.Register(
            nameof(ProgressValue),
            typeof(double),
            typeof(AdbSnackbar),
            new PropertyMetadata(0.0));

    public double ProgressValue
    {
        get => (double)GetValue(ProgressValueProperty);
        set => SetValue(ProgressValueProperty, value);
    }

    public static readonly DependencyProperty ProgressMaximumProperty =
        DependencyProperty.Register(
            nameof(ProgressMaximum),
            typeof(double),
            typeof(AdbSnackbar),
            new PropertyMetadata(100.0));

    public double ProgressMaximum
    {
        get => (double)GetValue(ProgressMaximumProperty);
        set => SetValue(ProgressMaximumProperty, value);
    }

    public static readonly DependencyProperty ProgressForegroundProperty =
        DependencyProperty.Register(
            nameof(ProgressForeground),
            typeof(Brush),
            typeof(AdbSnackbar),
            new PropertyMetadata(null));

    public Brush ProgressForeground
    {
        get => (Brush)GetValue(ProgressForegroundProperty);
        set => SetValue(ProgressForegroundProperty, value);
    }

    public static readonly DependencyProperty TimeRemainingProperty =
        DependencyProperty.Register(
            nameof(TimeRemaining),
            typeof(TimeSpan),
            typeof(AdbSnackbar),
            new PropertyMetadata(TimeSpan.Zero, OnTimeRemainingChanged));

    public TimeSpan TimeRemaining
    {
        get => (TimeSpan)GetValue(TimeRemainingProperty);
        set => SetValue(TimeRemainingProperty, value);
    }

    private static void OnTimeRemainingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is AdbSnackbar snackbar && e.NewValue is TimeSpan timeSpan)
            snackbar.ProgressValue = timeSpan.TotalSeconds;
    }

    protected override void OnPropertyChanged(DependencyPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);

        if (e.Property.Name == nameof(IsShown))
        {
            if ((bool)e.NewValue)
                StartCountdown();
            else
                StopCountdown();
        }
    }

    private void StartCountdown()
    {
        if (ProgressMode != SnackProgressMode.TimeOutDisplay || Timeout == TimeSpan.MaxValue)
            return;

        ProgressMaximum = Timeout.TotalSeconds;
        TimeRemaining = Timeout;

        _countdownTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
    }

    private void StopCountdown()
    {
        _countdownTimer?.Stop();
        _countdownTimer = null;
        TimeRemaining = TimeSpan.Zero;
    }

    private void OnCountdownTick(object sender, EventArgs e)
    {
        var remaining = TimeRemaining - TimeSpan.FromSeconds(1);
        TimeRemaining = remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;

        if (TimeRemaining == TimeSpan.Zero)
            StopCountdown();
    }
}
