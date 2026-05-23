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
        DependencyProperty.Register(nameof(IsUpVisible), typeof(bool),
          typeof(TransferIndicator), new PropertyMetadata(null));

    public bool IsDownVisible
    {
        get => (bool)GetValue(IsDownVisibleProperty);
        set => SetValue(IsDownVisibleProperty, value);
    }

    public static readonly DependencyProperty IsDownVisibleProperty =
        DependencyProperty.Register(nameof(IsDownVisible), typeof(bool),
          typeof(TransferIndicator), new PropertyMetadata(null));

    public bool ServerUnresponsive
    {
        get => (bool)GetValue(ServerUnresponsiveProperty);
        set => SetValue(ServerUnresponsiveProperty, value);
    }

    public static readonly DependencyProperty ServerUnresponsiveProperty =
        DependencyProperty.Register(nameof(ServerUnresponsive), typeof(bool),
          typeof(TransferIndicator), new PropertyMetadata(false, OnServerUnresponsiveChanged));

    private static void OnServerUnresponsiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (TransferIndicator)d;
        if ((bool)e.NewValue)
        {
            self.RootMenuItem.Items.Add(new AdbMenuItem
            {
                Header = "adb kill-server",
                FontFamily = (FontFamily)self.FindResource("ConsoleFont"),
                Style = (Style)self.FindResource("AdbMenuItemStyle"),
                Command = new RelayCommand(() => Task.Run(() => Services.ADBService.KillAdbServer())),
            });

            self.RootMenuItem.Items.Add(new AdbMenuItem
            {
                Header = "taskkill /f /im adb.exe",
                FontFamily = (FontFamily)self.FindResource("ConsoleFont"),
                Style = (Style)self.FindResource("AdbMenuItemStyle"),
                Command = new RelayCommand(() => Task.Run(() => Services.ADBService.KillAdbProcess())),
            });
        }
        else
        {
            self.RootMenuItem.Items.Clear();
        }
    }
}
