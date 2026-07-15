using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;
using System.Windows.Shell;
using Wpf.Ui.Controls;

namespace ADB_Explorer.ViewModels.Windows;

public partial class MainWindowViewModel : ObservableObject
{
    [ObservableProperty]
    public partial string ApplicationTitle { get; set; } = Properties.AppGlobal.AppDisplayName;

    private readonly NavigationViewItem _logItem = new()
    {
        Content = new Wpf.Ui.Controls.TextBlock() { FontSize = 12, Text = Strings.Resources.S_BUTTON_LOG, TextTrimming = TextTrimming.CharacterEllipsis },
        Icon = new FluentPathIcon { Data = FluentPathGeometries.TextBulletListSquare, Width = 22, Height = 22 },
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

    public bool IsNavigationEnabled => AdbHelper.CurrentAdbState.Status is AdbHelper.AdbStatus.Valid;

    [ObservableProperty]
    public partial TaskbarItemProgressState TaskbarItemProgress { get; set; }

    [ObservableProperty]
    public partial double TaskbarProgressValue { get; set; }

    [ObservableProperty]
    public partial string AdbReadRate { get; set; } = null;

    [ObservableProperty]
    public partial string AdbWriteRate { get; set; } = null;

    [ObservableProperty]
    public partial bool IsAdbReadActive { get; set; } = false;

    [ObservableProperty]
    public partial bool IsAdbWriteActive { get; set; } = false;

    [ObservableProperty]
    public partial bool ServerUnresponsive { get; set; } = false;

    [ObservableProperty]
    public partial string LastResponse { get; set; }

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

        AdbHelper.CurrentAdbState.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AdbHelper.CurrentAdbState.Status))
            {
                OnPropertyChanged(nameof(IsNavigationEnabled));
            }
        };

        Task.Run(InitNotifications);
    }

    public void UpdateFileOp()
    {
        if (Data.FileOpQ.AnyFailedOperations)
            TaskbarItemProgress = TaskbarItemProgressState.Error;
        else if (Data.FileOpQ.IsActive)
        {
            if (Data.FileOpQ.Progress == 0)
                TaskbarItemProgress = TaskbarItemProgressState.Indeterminate;
            else
            {
                TaskbarItemProgress = TaskbarItemProgressState.Normal;
                TaskbarProgressValue = Data.FileOpQ.Progress;
            }
        }
        else
            TaskbarItemProgress = TaskbarItemProgressState.None;
    }

    public async void InitNotifications()
    {
        if (Data.Settings.ShowLanguageNotification &&
            (Data.Settings.OriginalUICulture is null || Data.Settings.OriginalUICulture.Name != "en-US"))
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
                        Network.OpenUrl(Links.WEBLATE.ToString(), Data.RuntimeSettings.DefaultBrowserPath);

                    Data.Settings.ShowLanguageNotification = false;
                }, Strings.Resources.S_LANG_NOTIFICATION_TITLE,
                Notifications));
            });
        }

        var appVersion = Properties.AppGlobal.AppVersion;
        var isUpgrade = new Version(appVersion) > new Version(Data.Settings.LastVersion);

        if (isUpgrade)
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
                        Network.OpenUrl($"https://github.com/Alex4SSB/ADB-Explorer/releases/tag/v{appVersion}", Data.RuntimeSettings.DefaultBrowserPath);

                    Data.Settings.LastVersion = appVersion;
                }, Strings.Resources.S_NEW_VERSION_TITLE,
                Notifications));
            });

            if (Data.Settings.PrivacyCheckAppVersion != appVersion)
            {
                var privacyUpdatedTask = Network.GetPrivacyPolicyLastUpdatedAsync();
                var appVersionReleaseTask = Network.GetReleasePublishedDateAsync(appVersion);
                await Task.WhenAll(privacyUpdatedTask, appVersionReleaseTask);

                var privacyUpdated = await privacyUpdatedTask;
                var appVersionRelease = await appVersionReleaseTask;

                if (privacyUpdated is not null && appVersionRelease is not null)
                {
                    Data.Settings.PrivacyCheckAppVersion = appVersion;

                    if (appVersionRelease > privacyUpdated
                        && (Data.Settings.LastAcknowledgedPrivacyUpdate is null || privacyUpdated > Data.Settings.LastAcknowledgedPrivacyUpdate))
                    {
                        Data.Settings.PendingPrivacyUpdate = privacyUpdated;
                    }
                }
            }
        }

        if (Data.Settings.PendingPrivacyUpdate is not null)
        {
            var pendingPrivacyUpdate = Data.Settings.PendingPrivacyUpdate.Value;

            App.SafeInvoke(() =>
            {
                Notifications.Add(new(async () =>
                {
                    var res = await DialogService.ShowConfirmation(
                        Strings.Resources.S_PRIVACY_UPDATE_MSG,
                        Strings.Resources.S_PRIVACY_UPDATE_TITLE,
                        Strings.Resources.S_PRIVACY_POLICY,
                        cancelText: Strings.Resources.S_BUTTON_CLOSE,
                        icon: DialogService.DialogIcon.Informational);

                    if (res.Item1 is ContentDialogResult.Primary)
                        Network.OpenUrl(Links.ADB_EXPLORER_PRIVACY.ToString(), Data.RuntimeSettings.DefaultBrowserPath);

                    Data.Settings.LastAcknowledgedPrivacyUpdate = pendingPrivacyUpdate;
                    Data.Settings.PendingPrivacyUpdate = null;
                }, Strings.Resources.S_PRIVACY_UPDATE_TITLE,
                Notifications));
            });
        }

        if (Data.RuntimeSettings.IsAppPackaged 
            && !Data.RuntimeSettings.SkipAppDataNotification
            && !AppDataHelper.IsAppDataLocationChoiceMade())
        {
            App.SafeInvoke(() =>
            {
                Notifications.Add(new(async () =>
                {
                    var customPath = await AppDataHelper.PromptAppDataLocationChoiceAsync();
                    if (customPath is not null)
                        AppDataHelper.ApplyAppDataPath(customPath, App.Services);
                }, Strings.Resources.S_APP_DATA_LOCATION_TITLE,
                Notifications));
            });
        }

        if (!Data.RuntimeSettings.IsAppPackaged && Data.Settings.CheckForUpdates)
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
                        Network.OpenUrl($"https://github.com/Alex4SSB/ADB-Explorer/releases/tag/v{latestVersion}", Data.RuntimeSettings.DefaultBrowserPath);
                }, Strings.Resources.S_NEW_VERSION_TITLE,
                Notifications));
            });
        }
    }
}
