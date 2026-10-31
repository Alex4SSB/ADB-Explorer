using ADB_Explorer.Resources;

namespace ADB_Explorer.Controls;

/// <summary>
/// Notifications, ADB transfer indicator, and report-issue button hosted in the title bar
/// </summary>
public partial class TitleBarMenu : UserControl
{
    public static ICommand ReportIssueCommand { get; } = new RelayCommand(() =>
        Network.OpenUrl(Links.ADB_EXPLORER_ISSUES.ToString(), Data.RuntimeSettings.DefaultBrowserPath));

    public TitleBarMenu()
    {
        InitializeComponent();
    }

    public bool ServerUnresponsive
    {
        get => (bool)GetValue(ServerUnresponsiveProperty);
        set => SetValue(ServerUnresponsiveProperty, value);
    }

    public static readonly DependencyProperty ServerUnresponsiveProperty =
        DependencyProperty.Register(nameof(ServerUnresponsive), typeof(bool),
          typeof(TitleBarMenu), new PropertyMetadata(false, OnServerUnresponsiveChanged));

    private static void OnServerUnresponsiveChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var self = (TitleBarMenu)d;
        if ((bool)e.NewValue)
        {
            self.TransferMenuItem.Items.Add(new AdbMenuItem
            {
                Header = "adb kill-server",
                FontFamily = (FontFamily)self.FindResource("ConsoleFont"),
                Style = (Style)self.FindResource("AdbMenuItemStyle"),
                Command = new RelayCommand(() => Task.Run(() => Services.AdbService.KillAdbServer())),
            });

            self.TransferMenuItem.Items.Add(new AdbMenuItem
            {
                Header = "taskkill /f /im adb.exe",
                FontFamily = (FontFamily)self.FindResource("ConsoleFont"),
                Style = (Style)self.FindResource("AdbMenuItemStyle"),
                Command = new RelayCommand(() => Task.Run(() => Services.AdbService.KillAdbProcess())),
            });
        }
        else
        {
            self.TransferMenuItem.Items.Clear();
        }
    }

    public class Notification : BaseAction
    {
        public string Title { get; }

        public static string Tooltip => Strings.Resources.S_TOOLTIP_MORE_INFO;

        public Notification(Action action, string title, ObservableList<Notification> list) : base(() => true, action)
        {
            Title = title;

            ((CommandHandler)Command).OnExecute.PropertyChanged += (sender, e) =>
            {
                list.Remove(this);
            };
        }
    }
}
