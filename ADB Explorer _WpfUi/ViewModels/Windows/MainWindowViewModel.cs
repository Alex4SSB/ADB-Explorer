using Wpf.Ui.Controls;

namespace ADB_Explorer.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _applicationTitle = Properties.AppGlobal.AppDisplayName;

    [ObservableProperty]
    private ObservableCollection<object> _menuItems =
    [
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_SETTINGS_GROUP_EXPLORER, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uEC50" },
            TargetPageType = typeof(Views.Pages.DashboardPage),
            ToolTip = Strings.Resources.S_SETTINGS_GROUP_EXPLORER
        },
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_BUTTON_DEVICES, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uE8CC" },
            TargetPageType = typeof(Views.Pages.DataPage),
            ToolTip = Strings.Resources.S_BUTTON_DEVICES
        }
    ];

    [ObservableProperty]
    private ObservableCollection<object> _footerMenuItems =
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
}
