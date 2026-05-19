using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using Wpf.Ui.Controls;

namespace ADB_Explorer.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ApplicationTitle { get; set; } = Properties.AppGlobal.AppDisplayName;

    private readonly NavigationViewItem _logItem = new()
    {
        Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_BUTTON_LOG, TextTrimming = TextTrimming.CharacterEllipsis },
        Icon = new FontIcon { Glyph = "\uE9A4" },
        TargetPageType = typeof(Views.Pages.LogPage),
        ToolTip = Strings.Resources.S_BUTTON_LOG,
        Visibility = Data.Settings.EnableLog ? Visibility.Visible : Visibility.Collapsed
    };

    [ObservableProperty]
    public partial ObservableCollection<NavigationViewItem> MenuItems { get; set; } =
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
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_ACTION_OPERATION_PLURAL, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uEADF" },
            TargetPageType = typeof(Views.Pages.OperationsPage),
            ToolTip = Strings.Resources.S_ACTION_OPERATION_PLURAL
        }
    ];

    [ObservableProperty]
    public partial ObservableCollection<NavigationViewItem> FooterMenuItems { get; set; } =
    [
        new NavigationViewItem()
        {
            Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_SETTINGS_TITLE, TextTrimming = TextTrimming.CharacterEllipsis },
            Icon = new FontIcon { Glyph = "\uE713" },
            TargetPageType = typeof(Views.Pages.SettingsPage),
            ToolTip = Strings.Resources.S_SETTINGS_TITLE
        }
    ];

    [ObservableProperty]
    public partial ObservableList<Controls.NotificationBell.Notification> Notifications { get; set; } = [];

    public MainWindowViewModel()
    {
        MenuItems.Add(_logItem);

        Data.Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.EnableLog))
            {
                _logItem.Visibility = Data.Settings.EnableLog ? Visibility.Visible : Visibility.Collapsed;
            }
        };

        Task.Run(InitNotifications);
    }

    public async void InitNotifications()
    {
        if (Data.Settings.ShowLanguageNotification &&
            (Data.Settings.OriginalCulture is null || Data.Settings.OriginalCulture.Name != "en-US"))
        {
            App.SafeInvoke(() =>
            {
                Notifications.Add(new(async () =>
                {
                    var res = await DialogService.ShowConfirmation(Strings.Resources.S_LANG_NOTIFICATION,
                        Strings.Resources.S_LANG_NOTIFICATION_TITLE,
                        Strings.Resources.S_GOTO_WEBLATE,
                        cancelText: Strings.Resources.S_BUTTON_CLOSE,
                        icon: DialogService.DialogIcon.Informational);

                    if (res.Item1 is ContentDialogResult.Primary)
                        Process.Start(Data.RuntimeSettings.DefaultBrowserPath, $"\"{Links.WEBLATE}\"");

                    Data.Settings.ShowLanguageNotification = false;
                }, Strings.Resources.S_LANG_NOTIFICATION_TITLE,
                Notifications));
            });
        }

        if (new Version(Properties.AppGlobal.AppVersion) > new Version(Data.Settings.LastVersion))
        {
            App.SafeInvoke(() =>
            {
                Notifications.Add(new(async () =>
                {
                    var res = await DialogService.ShowConfirmation(
                        Strings.Resources.S_NEW_VERSION_MSG,
                        Strings.Resources.S_NEW_VERSION_TITLE,
                        Strings.Resources.S_GO_TO_RELEASE_NOTES,
                        cancelText: Strings.Resources.S_BUTTON_CLOSE);

                    if (res.Item1 is ContentDialogResult.Primary)
                        Process.Start(Data.RuntimeSettings.DefaultBrowserPath, $"\"https://github.com/Alex4SSB/ADB-Explorer/releases/tag/v{Properties.AppGlobal.AppVersion}\"");

                    Data.Settings.LastVersion = Properties.AppGlobal.AppVersion;
                }, Strings.Resources.S_NEW_VERSION_TITLE,
                Notifications));
            });
        }

        if (!Data.RuntimeSettings.IsAppDeployed && Data.Settings.CheckForUpdates)
        {
            var latestVersion = await Network.LatestAppReleaseAsync();
            if (latestVersion is null || latestVersion <= Data.AppVersion)
                return;

            App.SafeInvoke(() =>
            {
                Notifications.Add(new(async () =>
                {
                    var res = await DialogService.ShowConfirmation(string.Format(Strings.Resources.S_NEW_VERSION, Properties.AppGlobal.AppDisplayName, latestVersion),
                        Strings.Resources.S_NEW_VERSION_TITLE,
                        Strings.Resources.S_GO_TO_VERSION_PAGE,
                        cancelText: Strings.Resources.S_BUTTON_CLOSE,
                        icon: DialogService.DialogIcon.Informational);

                    if (res.Item1 is ContentDialogResult.Primary)
                        Process.Start(Data.RuntimeSettings.DefaultBrowserPath, $"\"https://github.com/Alex4SSB/ADB-Explorer/releases/tag/v{latestVersion}\"");
                }, Strings.Resources.S_NEW_VERSION_TITLE,
                Notifications));
            });
        }
    }
}
