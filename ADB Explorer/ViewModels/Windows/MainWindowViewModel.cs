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

    /// <summary>Whether the active tab shows the Explorer page - the pane's bottom pages collapse into "More" then.</summary>
    [ObservableProperty]
    public partial bool IsExplorerPage { get; set; }

    partial void OnIsExplorerPageChanged(bool value) => UpdateFooterVisibility();

    /// <summary>Whether the pointer is over the pane's bottom items - the collapsed pages show again meanwhile.</summary>
    [ObservableProperty]
    public partial bool IsFooterHovered { get; set; }

    partial void OnIsFooterHoveredChanged(bool value) => UpdateFooterVisibility();

    private static NavigationViewItem CreateItem(string title, IconElement icon, Type? pageType = null) => new()
    {
        Content = new Wpf.Ui.Controls.TextBlock() { Text = title, TextTrimming = TextTrimming.CharacterEllipsis },
        Icon = icon,
        TargetPageType = pageType,
    };

    /// <summary>Never shown - only lets the NavigationView select something (and so deselect the others) on the Explorer page.</summary>
    private readonly NavigationViewItem _explorerItem = new()
    {
        Content = Strings.Resources.S_SETTINGS_GROUP_EXPLORER,
        TargetPageType = typeof(Views.Pages.ExplorerPage),
        Visibility = Visibility.Collapsed,
    };

    private readonly NavigationViewItem _devicesItem = CreateItem(Strings.Resources.S_BUTTON_DEVICES, new FontIcon { Glyph = "\uE8CC" }, typeof(Views.Pages.DevicesPage));

    private readonly NavigationViewItem _terminalItem = CreateItem(Strings.Resources.S_TERMINAL, new FontIcon { Glyph = "\uE756" }, typeof(Views.Pages.TerminalPage));

    private readonly NavigationViewItem _operationsItem = CreateItem(Strings.Resources.S_ACTION_OPERATION_PLURAL, new FontIcon { Glyph = "\uEADF" }, typeof(Views.Pages.OperationsPage));

    private readonly NavigationViewItem _logItem = CreateItem(Strings.Resources.S_BUTTON_LOG, new FluentPathIcon { Data = FluentPathGeometries.TextBulletListSquare, Width = 16, Height = 16 }, typeof(Views.Pages.LogPage));

    /// <summary>Stands in for the four items above on the Explorer page, until the pointer is over the pane's bottom.</summary>
    private readonly NavigationViewItem _moreItem = CreateItem(Strings.Resources.S_MENU_MORE, new FontIcon { Glyph = "\uE712" });

    /// <summary>Sets Settings apart from the pages above it; hidden together with them.</summary>
    private readonly NavigationViewItemSeparator _settingsSeparator = new();

    /// <summary>Pane top: only the hidden Explorer item - every visible page sits below the tree, in <see cref="FooterMenuItems"/>.</summary>
    [ObservableProperty]
    public partial ObservableCollection<NavigationViewItem> MenuItems { get; set; } = [];

    [ObservableProperty]
    public partial ObservableCollection<object> FooterMenuItems { get; set; } = [];

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
        MenuItems.Add(_explorerItem);

        FooterMenuItems.Add(_devicesItem);
        FooterMenuItems.Add(_terminalItem);
        FooterMenuItems.Add(_operationsItem);
        FooterMenuItems.Add(_logItem);
        FooterMenuItems.Add(_moreItem);
        FooterMenuItems.Add(_settingsSeparator);
        FooterMenuItems.Add(CreateItem(Strings.Resources.S_SETTINGS_TITLE, new FontIcon { Glyph = "\uE713" }, typeof(Views.Pages.SettingsPage)));

        UpdateFooterVisibility();

        Data.Settings.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AppSettings.EnableLog))
                App.SafeInvoke(UpdateFooterVisibility);
        };

        AdbHelper.CurrentAdbState.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(AdbHelper.CurrentAdbState.Status))
            {
                OnPropertyChanged(nameof(IsNavigationEnabled));
                App.SafeInvoke(UpdateFooterVisibility);
            }
        };

        Task.Run(InitNotifications);
    }

    private static Visibility Show(bool visible) => visible ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>On the Explorer page the pages collapse into "More", to leave the tree the room - unless the pointer is over them.</summary>
    private void UpdateFooterVisibility()
    {
        var enabled = IsNavigationEnabled;
        var expanded = enabled && (!IsExplorerPage || IsFooterHovered);

        PaneItemAnimation.SetShown(_devicesItem, expanded);
        PaneItemAnimation.SetShown(_terminalItem, expanded);
        PaneItemAnimation.SetShown(_operationsItem, expanded);
        PaneItemAnimation.SetShown(_logItem, expanded && Data.Settings.EnableLog);
        PaneItemAnimation.SetShown(_moreItem, enabled && !expanded);
        _settingsSeparator.Visibility = Show(enabled);
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

        if (!Data.RuntimeSettings.IsAppPackaged
            && Data.Settings.CheckForUpdates is not AppSettings.UpdatesMode.Off)
        {
            var latestRelease = await Network.LatestAppReleaseAsync();
            if (latestRelease is not { } release || release.Version <= Data.AppVersion)
                return;

            if (Data.Settings.CheckForUpdates is AppSettings.UpdatesMode.Update
                && release.PortableArchiveUrl is { } archiveUrl)
            {
                var updatePath = Path.Combine(AppContext.BaseDirectory, AdbExplorerConst.UPDATE_ARCHIVE_FILE);
                if (await Network.DownloadFileAsync(archiveUrl, updatePath))
                {
                    App.SafeBeginInvoke(() => _ = PromptDownloadedUpdateAsync(release.Version));
                    return;
                }
            }

            App.SafeInvoke(() =>
            {
                Notifications.Add(new(async () =>
                {
                    var res = await DialogService.ShowConfirmation(string.Format(Strings.Resources.S_NEW_VERSION, Properties.AppGlobal.AppDisplayName, release.Version),
                        Strings.Resources.S_NEW_VERSION_TITLE,
                        Strings.Resources.S_GO_TO_VERSION_PAGE,
                        cancelText: Strings.Resources.S_BUTTON_CLOSE,
                        icon: DialogService.DialogIcon.Informational);

                    if (res.Item1 is ContentDialogResult.Primary)
                        Network.OpenUrl($"https://github.com/Alex4SSB/ADB-Explorer/releases/tag/v{release.Version}", Data.RuntimeSettings.DefaultBrowserPath);
                }, Strings.Resources.S_NEW_VERSION_TITLE,
                Notifications));
            });
        }
    }

    private static async Task PromptDownloadedUpdateAsync(Version latestVersion)
    {
        var result = await DialogService.ShowDialog(
            string.Format(Strings.Resources.S_NEW_VERSION_READY, Properties.AppGlobal.AppDisplayName, latestVersion),
            Strings.Resources.S_NEW_VERSION_TITLE,
            Strings.Resources.S_RESTART,
            closeText: Strings.Resources.S_UPDATE_ON_CLOSE);

        var restartNow = result is ContentDialogResult.Primary;
        if (!AppUpdateHelper.ApplyPendingUpdate(restartNow))
        {
            DialogService.ShowMessage(
                AppUpdateHelper.LastFailureReason ?? Strings.Resources.S_UPDATE_APPLY_FAILED,
                Strings.Resources.S_NEW_VERSION_TITLE,
                DialogService.DialogIcon.Exclamation);
        }
    }
}
