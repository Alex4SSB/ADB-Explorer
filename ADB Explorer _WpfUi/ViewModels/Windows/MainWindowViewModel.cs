using ADB_Explorer.Models;
using Wpf.Ui.Controls;

namespace ADB_Explorer.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = Properties.AppGlobal.AppDisplayName;

    private readonly NavigationViewItem _logItem = new()
    {
        Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_BUTTON_LOG, TextTrimming = TextTrimming.CharacterEllipsis },
        Icon = new FontIcon { Glyph = "\uE9A4" },
        TargetPageType = typeof(Views.Pages.LogPage),
        ToolTip = Strings.Resources.S_BUTTON_LOG,
        Visibility = Data.Settings.EnableLog ? Visibility.Visible : Visibility.Collapsed
    };

    [ObservableProperty]
    private ObservableCollection<NavigationViewItem> _menuItems =
    [
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_SETTINGS_GROUP_EXPLORER, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uEC50" },
            TargetPageType = typeof(Views.Pages.ExplorerPage),
            ToolTip = Strings.Resources.S_SETTINGS_GROUP_EXPLORER
        },
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_BUTTON_DEVICES, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uE8CC" },
            TargetPageType = typeof(Views.Pages.DevicesPage),
            ToolTip = Strings.Resources.S_BUTTON_DEVICES
        },
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_TERMINAL, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uE756" },
            TargetPageType = typeof(Views.Pages.TerminalPage),
            ToolTip = Strings.Resources.S_TERMINAL
        },
    ];

    [ObservableProperty]
    private ObservableCollection<NavigationViewItem> _footerMenuItems =
    [
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_SETTINGS_TITLE, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uE713" },
            TargetPageType = typeof(Views.Pages.SettingsPage),
            ToolTip = Strings.Resources.S_SETTINGS_TITLE
        }
    ];

    //[ObservableProperty]
    //private ObservableCollection<Wpf.Ui.Controls.MenuItem> _trayMenuItems = new()
    //{
    //    new Wpf.Ui.Controls.MenuItem { Header = "Home", Tag = "tray_home" }
    //};

    public MainWindowViewModel()
    {
        MenuItems.Add(_logItem);

        Data.Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(Services.AppSettings.EnableLog))
            {
                _logItem.Visibility = Data.Settings.EnableLog ? Visibility.Visible : Visibility.Collapsed;
            }
        };
    }
}
