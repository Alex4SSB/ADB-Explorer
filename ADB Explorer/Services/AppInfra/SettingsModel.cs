using ADB_Explorer.Controls;
using ADB_Explorer.Helpers;
using ADB_Explorer.Properties;
using ADB_Explorer.ViewModels;
using System.Linq.Expressions;
using static ADB_Explorer.Models.Data;
using static ADB_Explorer.Services.SettingsAction;
using ControlAppearance = Wpf.Ui.Controls.ControlAppearance;

namespace ADB_Explorer.Services;

public static class UISettings
{
    public static ObservableList<AbstractGroup> SettingsList { get; set; }

    public static IEnumerable<AbstractSetting> SortSettings => SettingsList.SelectMany(group => group.Children)
        .Where(set => set.Visibility is Visibility.Visible && set.Description != Properties.AppGlobal.AppDisplayName)
        .OrderBy(sett => sett.Description).Prepend(SettingsList.SelectMany(group => group.Children).First(s => s.Description == Properties.AppGlobal.AppDisplayName));

    private static readonly Dictionary<ActionType, string> Icons = new()
    {
        { ActionType.ChangeDefaultPath, "\uE70F" },
        { ActionType.ClearDefaultPath, "\uE711" },
        { ActionType.ResetApp, "\uE72C" },
    };

    private static readonly List<SettingsAction> SettingsActions =
    [
        new(ActionType.ChangeDefaultPath, () => true, SettingsHelper.ChangeDefaultPathAction, Icons[ActionType.ChangeDefaultPath], Strings.Resources.S_BUTTON_CHANGE),
        new(ActionType.ClearDefaultPath, () => !string.IsNullOrEmpty(Settings.DefaultFolder), () => Settings.DefaultFolder = "", Icons[ActionType.ClearDefaultPath], Strings.Resources.S_BUTTON_CLEAR),
        new(ActionType.ChangeAdbPath, () => true, SettingsHelper.ChangeAdbPathAction, Icons[ActionType.ChangeDefaultPath], Strings.Resources.S_BUTTON_CHANGE),
        new(ActionType.ClearAdbPath, () => !string.IsNullOrEmpty(Settings.ManualAdbPath), () => Settings.ManualAdbPath = "", Icons[ActionType.ClearDefaultPath], Strings.Resources.S_BUTTON_CLEAR),
        new(ActionType.ResetApp, () => true, SettingsHelper.ResetAppAction, Icons[ActionType.ResetApp], Strings.Resources.S_RESTART_APP),
    ];

    /// <summary>
    /// The Weblate logo as a DrawingImage, translated from the SVG version.
    /// </summary>
    private static DrawingImage WeblateLogo
    {
        get
        {
            LinearGradientBrush gradient_A = new()
            {
                StartPoint = new(0.0, 0.38057),
                EndPoint = new(0.6826, 0.38057),
                GradientStops =
                {
                    new((Color)ColorConverter.ConvertFromString("#00D2E6"), 0.0),
                    new((Color)ColorConverter.ConvertFromString("#2ECCAA"), 1.0)
                },
            };

            LinearGradientBrush gradient_B = new()
            {
                StartPoint = new(0.17275, 0.59719),
                EndPoint = new(0.79, 0.30635),
                GradientStops =
                {
                    new(Color.FromArgb(0, 0, 0, 0), 0.0),
                    new(Color.FromArgb(255, 0, 0, 0), 0.514),
                    new(Color.FromArgb(0, 0, 0, 0), 1.0)
                },
                Opacity = 0.3,
            };

            LinearGradientBrush gradient_C = new()
            {
                StartPoint = new(1.0, 0.39719),
                EndPoint = new(0.31855, 0.39719),
                GradientStops =
                {
                    new((Color)ColorConverter.ConvertFromString("#1FA385"), 0.0),
                    new((Color)ColorConverter.ConvertFromString("#2ECCAA"), 1.0)
                },
            };

            Geometry geometry_A = Geometry.Parse("m 127.25 111.61 c -2.8884 -0.014533 -5.7666 -0.6024 -8.4797 -1.7847 c -6.1117 -2.6626 -11.493 -7.6912 -15.872 -14.495 c 1.2486 -2.2193 2.3738 -4.5173 3.3784 -6.8535 c 4.4051 -10.243 6.5 -21.46 6.6607 -32.593 c -0.023342 -0.22083 -0.041627 -0.44244 -0.055243 -0.66483 l -0.01206 -0.57132 c -0.009726 -4.3654 -0.67459 -8.7898 -2.1767 -12.909 c -1.7304 -4.7458 -4.4887 -9.4955 -8.865 -11.348 c -0.79519 -0.33595 -1.6316 -0.47701 -2.4642 -0.45737 c -5.5049 -10.289 -5.6799 -20.149 0 -29.537 c 0.10115 0 0.20619 0.00039293 0.30734 0.0011788 c 6.7012 0.07387 13.34 2.1418 19.021 5.7536 c 15.469 9.835 23.182 29.001 23.352 47.818 c 0.002334 0.22083 -0.000389 0.44126 -0.007003 0.66169 h 0.086756 c -0.022554 19.887 -4.8049 40.054 -14.875 56.979 Z m -34.3 31.216 c -14.448 5.9425 -31.228 5.6236 -45.549 -1.025 c -16.476 -7.6475 -29.065 -22.512 -36.818 -39.479 c -13.262 -29.022 -13.566 -63.715 -0.98815 -93.182 c 9.4458 3.7788 17.845 -2.2397 17.845 -2.2397 s -0.019452 9.2605 8.9478 13.905 c -9.2007 21.556 -8.979 47.167 0.2412 68.173 c 4.4389 10.107 11.22 19.519 20.619 24.842 c 3.3547 1.8996 7.041 3.126 10.833 3.5862 c 0.014037 0.021941 0.028081 0.043876 0.042134 0.065804 c 6.6965 10.449 15.132 19.157 24.828 25.354 Z");
            Geometry geometry_B = Geometry.Parse("m 127.24 111.61 c -2.8869 -0.015092 -5.7636 -0.60296 -8.4755 -1.7847 c -6.1127 -2.663 -11.495 -7.6928 -15.874 -14.498 c 1.2494 -2.2205 2.3754 -4.5198 3.3806 -6.8572 c 1.3282 -3.0884 2.4463 -6.2648 3.3644 -9.501 c 2.128 -7.4978 30.382 2.0181 26.072 14.371 c -2.2239 6.373 -5.0394 12.509 -8.4675 18.27 Z m -34.302 31.212 c -14.446 5.9396 -31.224 5.6198 -45.543 -1.0278 c -16.476 -7.6475 0.44739 -33.303 9.8465 -27.981 c 3.3533 1.8988 7.0378 3.125 10.828 3.5856 c 0.01567 0.024498 0.03135 0.048989 0.04704 0.073472 c 6.695 10.447 15.128 19.153 24.821 25.349 Z");
            Geometry geometry_C = Geometry.Parse("m 56.762 54.628 c -0.0066136 -0.22043 -0.0093369 -0.44086 -0.0070027 -0.66169 c 0.17001 -18.817 7.8827 -37.983 23.352 -47.818 c 5.6811 -3.6118 12.32 -5.6798 19.021 -5.7536 c 0.10115 -0.00078585 0.20619 -0.0011788 0.30734 -0.0011788 v 29.537 c -0.83254 -0.019646 -1.669 0.12141 -2.4642 0.45737 c -4.3763 1.8523 -7.1345 6.602 -8.865 11.348 c -1.5021 4.1191 -2.1669 8.5434 -2.1767 12.909 l -0.01206 0.57132 c -0.013616 0.2224 -0.031901 0.44401 -0.055243 0.66483 c 0.16067 11.134 2.2556 22.35 6.6607 32.593 c 4.9334 11.472 12.775 22.025 23.847 26.849 c 8.3526 3.6397 17.612 2.7811 25.182 -1.5057 c 9.3991 -5.3226 16.18 -14.734 20.619 -24.842 c 9.2202 -21.006 9.4419 -46.617 0.2412 -68.173 c 8.9673 -4.6444 8.9478 -13.905 8.9478 -13.905 s 8.3993 6.0185 17.845 2.2397 c 12.578 29.466 12.274 64.16 -0.98815 93.182 c -7.7535 16.967 -20.343 31.831 -36.818 39.479 c -14.667 6.809 -31.913 6.9792 -46.591 0.58389 c -13.19 -5.7489 -23.918 -16.106 -31.637 -28.15 c -11.179 -17.443 -16.472 -38.678 -16.496 -59.604 Z");

            DrawingGroup drawingGroup = new()
            {
                Children =
                {
                    new GeometryDrawing(gradient_A, null, geometry_A),
                    new GeometryDrawing(gradient_B, null, geometry_B),
                    new GeometryDrawing(gradient_C, null, geometry_C),
                }
            };

            drawingGroup.Freeze();

            var imageSource = new DrawingImage(drawingGroup);
            imageSource.Freeze();

            return imageSource;
        }
    }

    /// <summary>
    /// The GitHub logo as a Geometry, translated from the SVG version.
    /// </summary>
    private static readonly Geometry GitHubGeometry = Geometry.Parse("M 48.854 0 C 21.839 0 0 22 0 49.217 c 0 21.756 13.993 40.172 33.405 46.69 c 2.427 0.49 3.316 -1.059 3.316 -2.362 c 0 -1.141 -0.08 -5.052 -0.08 -9.127 c -13.59 2.934 -16.42 -5.867 -16.42 -5.867 c -2.184 -5.704 -5.42 -7.17 -5.42 -7.17 c -4.448 -3.015 0.324 -3.015 0.324 -3.015 c 4.934 0.326 7.523 5.052 7.523 5.052 c 4.367 7.496 11.404 5.378 14.235 4.074 c 0.404 -3.178 1.699 -5.378 3.074 -6.6 c -10.839 -1.141 -22.243 -5.378 -22.243 -24.283 c 0 -5.378 1.94 -9.778 5.014 -13.2 c -0.485 -1.222 -2.184 -6.275 0.486 -13.038 c 0 0 4.125 -1.304 13.426 5.052 a 46.97 46.97 0 0 1 12.214 -1.63 c 4.125 0 8.33 0.571 12.213 1.63 c 9.302 -6.356 13.427 -5.052 13.427 -5.052 c 2.67 6.763 0.97 11.816 0.485 13.038 c 3.155 3.422 5.015 7.822 5.015 13.2 c 0 18.905 -11.404 23.06 -22.324 24.283 c 1.78 1.548 3.316 4.481 3.316 9.126 c 0 6.6 -0.08 11.897 -0.08 13.526 c 0 1.304 0.89 2.853 3.316 2.364 c 19.412 -6.52 33.405 -24.935 33.405 -46.691 C 97.707 22 75.788 0 48.854 0 Z");

    private static List<AbstractSetting> BuildAboutSettings()
    {
        var settings = new List<AbstractSetting>
        {
            new InfoSetting(AppGlobal.AppDisplayName, null, (FontFamily)App.Current.Resources["Nunito"], 18, $"v{AppGlobal.AppVersion}", TextAlignment.Center),
            new LinkSetting(Strings.Resources.S_DONATE, Resources.Links.SPONSOR, new("\uEB51", brush: "SponsorIconBrush")),
            new LinkSetting(Strings.Resources.S_APP_DATA_FOLDER, new(AppDataPath), new(FluentPathGeometries.ItemPath, flowDirection: FlowDirection.LeftToRight), resolveFilePath: () => AppDataPath),
            new LinkSetting(Strings.Resources.S_GITHUB_REPO, Resources.Links.ADB_EXPLORER_GITHUB, new(GitHubGeometry)),
            new LinkSetting(Strings.Resources.S_GOTO_WEBLATE, Resources.Links.WEBLATE, new(WeblateLogo)),
            new LinkSetting(Strings.Resources.S_PRIVACY_POLICY, Resources.Links.ADB_EXPLORER_PRIVACY, new("\uE72E")),
            new LinkSetting(RuntimeSettings.IsAppPackaged ? Strings.Resources.S_ADB_LEARN_MORE : Strings.Resources.S_ADB_DOWNLOAD, Resources.Links.L_ADB_PAGE, new(FileToIconConverter.LoadBitmap(AppGlobal.icons8_android_os_94))),
        };

        if (!RuntimeSettings.IsAppPackaged)
        {
            settings.Add(new SimpleComboSetting<AppSettings.UpdatesMode>(() => Settings.CheckForUpdates, Strings.Resources.S_SETTINGS_UPDATES, [
                new(AppSettings.UpdatesMode.Off, Strings.Resources.S_SETTINGS_INACTIVE),
                new(AppSettings.UpdatesMode.Check, Strings.Resources.S_SETTINGS_UPDATES_CHECK),
                new(AppSettings.UpdatesMode.Update, Strings.Resources.S_SETTINGS_UPDATES_UPDATE),
            ], icon: new("\uE895")));
        }

        if (CrashReportService.IsConfigured)
        {
            settings.Add(new BoolSetting(() => Settings.ShowMessageOnCrash, Strings.Resources.S_SETTINGS_CRASH_DIALOG, icon: new("\uE783")));
            settings.Add(new LongDescriptionSetting(Strings.Resources.S_CRASH_REPORTING_TITLE, Strings.Resources.S_CRASH_REPORTING_NOTICE, new("\uE783")));
        }

        settings.Add(new MultiLinkSetting(Strings.Resources.S_ATTRIBUTIONS, [
            new("WpfUi", Resources.Links.WPF_UI),
            new("AdvancedSharpAdb", Resources.Links.ADVANCED_SHARP_ADB),
            new("Vanara", Resources.Links.VANARA),
            new("QRCoder", Resources.Links.QR_CODER),
            new("Emoji.Wpf", Resources.Links.EMOJI_WPF),
            new("AvalonEdit", Resources.Links.AVALONEDIT),
            new("CommunityToolkit.Mvvm", Resources.Links.MVVM_TOOLKIT),
            new("WindowsAPICodePack", Resources.Links.API_CODEPACK),
            new("Newtonsoft.Json", Resources.Links.JSON),
            new("Fluent System Icons", Resources.Links.FLUENT_SYSTEM_ICONS),
            new("Icons8", Resources.Links.ICONS8),
            new("Vecteezy", Resources.Links.VECTEEZY),
            new("Grafana Labs", Resources.Links.GRAFANA_LABS),
            new("LGPL v3", Resources.Links.LGPL3),
            new("Apache", Resources.Links.L_APACHE_LIC),
            new(Strings.Resources.S_CC_NAME, Resources.Links.L_CC_LIC),
        ], new("\uE90F")));
        settings.Add(new LongDescriptionSetting(Strings.Resources.S_ANDROID_ICONS_TITLE, $"{Strings.Resources.S_ANDROID_ROBOT_LIC}\n\n{Strings.Resources.S_APK_ICON_LIC}", new("\uE946")));

        return settings;
    }

    private static string[] sizes => [
        Strings.Resources.BYTES.Trim('{', '}', '0', ' ', TextHelper.LTR_MARK, TextHelper.RTL_MARK), 
        Strings.Resources.KILO.Trim('{', '}', '0', ' ', TextHelper.LTR_MARK, TextHelper.RTL_MARK), 
        Strings.Resources.MEGA.Trim('{', '}', '0', ' ', TextHelper.LTR_MARK, TextHelper.RTL_MARK), 
        Strings.Resources.GIGA.Trim('{', '}', '0', ' ', TextHelper.LTR_MARK, TextHelper.RTL_MARK)
        ];

    public static void Init()
    {
        SettingsList =
        [
            new SettingsGroup("ADB",
            [
                new BoolSetting(() => Settings.EnableMdns, Strings.Resources.S_SETTINGS_ENABLE_MDNS, icon: new("\uED14")),
                new BoolSetting(() => Settings.PollDevices, Strings.Resources.S_SETTINGS_POLL_DEVICES, icon: new("\uEBDE")),
                new BoolSetting(() => Settings.PollBattery, Strings.Resources.S_SETTINGS_POLL_BATTERY, icon: new("\uEE63")),
                new BoolSetting(() => Settings.KillAdbOnExit, Strings.Resources.S_SETTINGS_KILL_ADB_ON_EXIT, icon: new("\uF71D")),
                new BoolSetting(() => Settings.EnableLog, Strings.Resources.S_BUTTON_LOG, icon: new(FluentPathGeometries.TextBulletListSquare))
                    { CardAppearance = ControlAppearance.Caution },
            ], new("\uE8CC")),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_DEVICE,
            [
                new BoolSetting(() => Settings.AutoRoot, Strings.Resources.S_SETTINGS_AUTO_ROOT, icon: new("\uE7EF")),
                new BoolSetting(() => Settings.SaveDevices, Strings.Resources.S_SETTINGS_SAVE_DEVICES, icon: new("\uE78C")),
                new BoolSetting(() => Settings.AutoOpen, Strings.Resources.S_SETTINGS_AUTO_OPEN, icon: new("\uE838")),
            ], new("\uE8EA")),
            new SettingsGroup(Strings.Resources.S_FILE_OP_TOOLTIP,
            [
                new BoolSetting(() => Settings.StopPollingOnSync, Strings.Resources.S_SETTINGS_STOP_ON_SYNC, icon: new("\uE8D8")),
                new BoolSetting(() => Settings.AllowMultiOp, Strings.Resources.S_SETTINGS_PARALLEL_OPERATIONS, icon: new("\uE762")),
                new BoolSetting(() => Settings.RescanOnPush, Strings.Resources.S_SETTINGS_MEDIA_RESCAN, icon: new("\uE7C5")),
                new BoolSetting(() => Settings.KeepDateModified, Strings.Resources.S_SETTINGS_KEEP_MODIFIED_DATE, icon: new("\uEC92")),
            ], new("\uEADF")),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_DRIVES,
            [
                new BoolSetting(() => Settings.PollDrives, Strings.Resources.S_SETTINGS_POLL_DRIVES, icon: new("\uEBC4")),
                new BoolSetting(() => Settings.EnableRecycle, Strings.Resources.S_DRIVE_TRASH, icon: new("\uE74D")),
                new BoolSetting(() => Settings.EnableBusyBox, Strings.Resources.S_SETTINGS_BUSYBOX, icon: new("\uF133")),
                new BoolSetting(() => Settings.EnableWsa, Strings.Resources.S_SETTINGS_WSA, icon: new("\uE78A")),
                new BoolSetting(() => Settings.EnableEmulatorDiscovery, Strings.Resources.S_SETTINGS_EMULATOR_DISCOVERY, icon: new("\uE99A")),
            ], new("\uE8CE")),
            new SettingsGroup("APK",
            [
                new BoolSetting(() => Settings.EnableApk, Strings.Resources.S_SETTINGS_APK, icon: new(FluentPathGeometries.BoxCheckmark, flowDirection: FlowDirection.LeftToRight)),
                new BoolSetting(() => Settings.ShowSystemPackages, Strings.Resources.S_SETTINGS_SYSTEM_APPS, visibleProp: AbstractSetting.ExtractPropertyInfo(() => Settings.EnableApk), icon: new(FluentPathGeometries.BoxToolbox)),
            ], new(FluentPathGeometries.Box)),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_EXPLORER,
            [
                new BoolSetting(() => Settings.ShowExtensions, Strings.Resources.S_SETTINGS_SHOW_EXTENSIONS, icon: new("\uE8AC")),
                new BoolSetting(() => Settings.ShowHiddenItems, Strings.Resources.S_SETTINGS_HIDDEN_ITEMS, icon: new("\uE8FF")),
                new BoolSetting(() => Settings.SortingPerLocation, Strings.Resources.S_SETTINGS_SORTING_PER_LOCATION, icon: new("\uE8CB")),
                new NumericSetting(() => Settings.MaxPreviewFileSize,
                                   Strings.Resources.S_SETTINGS_PREVIEW_MAX_SIZE,
                                   0,
                                   100000,
                                   sizes[1],
                                   icon: new("\uE1A5")),
                new SimpleComboSetting<AppSettings.FileSizeDisplay>(() => Settings.FileSizeMode, Strings.Resources.S_SETTINGS_FILE_SIZE_MODE,
                [
                    new(AppSettings.FileSizeDisplay.B, sizes[0]),
                    new(AppSettings.FileSizeDisplay.K, sizes[1]),
                    new(AppSettings.FileSizeDisplay.KM, $"{sizes[1]}/{sizes[2]}"),
                    new(AppSettings.FileSizeDisplay.KMG, $"{sizes[1]}/{sizes[2]}/{sizes[3]}"),
                    ]),
                new NumericSetting(() => Settings.FileSizeDecimal, 
                                   Strings.Resources.S_SETTINGS_FILE_SIZE_DECIMAL,
                                   0,
                                   9,
                                   icon: new(FluentPathGeometries.IncreaseDecimalPlaces)),
                new BoolSetting(() => Settings.DoubleClickToPull, Strings.Resources.S_SETTINGS_PULL_ON_DOUBLE_CLICK, AbstractSetting.ExtractPropertyInfo(() => Settings.DefaultFolder), new("\uE7C9")),
            ], new("\uEC50")),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_ICONS,
            [
                new SimpleComboSetting<AppSettings.ThumbnailMode>(() => Settings.ThumbsMode, Strings.Resources.S_SETTINGS_THUMBNAIL_MODE, [
                    new(AppSettings.ThumbnailMode.Off, Strings.Resources.S_SETTINGS_INACTIVE),
                    new(AppSettings.ThumbnailMode.IconViewOnly, Strings.Resources.S_SETTINGS_THUMBS_ICON_VIEW),
                    new(AppSettings.ThumbnailMode.OnPhotoDir, Strings.Resources.S_SETTINGS_THUMBS_PHOTO_DIR),
                    new(AppSettings.ThumbnailMode.OnConnect, Strings.Resources.S_SETTINGS_THUMBS_CONNECT) ],
                    icon: new("\uE15A")),
                new BoolSetting(() => Settings.MovieThumbsEnabled, Strings.Resources.S_SETTINGS_VIDEO_THUMBNAILS, AbstractSetting.ExtractPropertyInfo(() => Settings.ThumbsMode), new("\uE8B2")),
                new BoolSetting(() => Settings.ThumbSizePerLocation, Strings.Resources.S_SETTINGS_THUMB_SIZE_PER_LOCATION, AbstractSetting.ExtractPropertyInfo(() => Settings.ThumbsMode), new(FluentPathGeometries.ResizeImage)),
                new BoolSetting(() => Settings.PersistThumbs, Strings.Resources.S_SETTINGS_PERSIST_THUMBS, AbstractSetting.ExtractPropertyInfo(() => Settings.ThumbsMode), new("\uE78C")),
                new SimpleComboSetting<AppSettings.ThumbnailAge>(() => Settings.ThumbsAge, Strings.Resources.S_SETTINGS_THUMBS_AGE, [
                    new(AppSettings.ThumbnailAge.Disabled, Strings.Resources.S_SETTINGS_INACTIVE),
                    new(AppSettings.ThumbnailAge.OneMonth, Strings.Resources.S_ONE_MONTH),
                    new(AppSettings.ThumbnailAge.OneWeek, Strings.Resources.S_ONE_WEEK),
                    new(AppSettings.ThumbnailAge.OneDay, Strings.Resources.S_ONE_DAY),
                    new(AppSettings.ThumbnailAge.OneHour, Strings.Resources.S_ONE_HOUR)],
                    visibleProp: AbstractSetting.ExtractPropertyInfo(() => Settings.ThumbsMode),
                    icon: new("\uE823")),
                new NumericSetting(() => Settings.MaxCustomThumbWeight,
                                   Strings.Resources.S_SETTINGS_MAX_CUSTOM_THUMB_WEIGHT,
                                   0,
                                   1000,
                                   sizes[1],
                                   AbstractSetting.ExtractPropertyInfo(() => Settings.ThumbsMode),
                                   new("\uEE71")),
                new BoolSetting(() => Settings.LimitThumbsPullSpeed, Strings.Resources.S_SETTINGS_THUMBS_THROTTLE, AbstractSetting.ExtractPropertyInfo(() => Settings.ThumbsMode), new("\uEC48")),
                new BoolSetting(() => Settings.SpecialFolderIcons, Strings.Resources.S_SETTINGS_SPECIAL_DIR_ICONS, icon: new("\uEC25")),
            ], new("\uE8B9")),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_WORK_DIRS,
            [
                new TextboxSetting(() => Settings.ManualAdbPath,
                                  Strings.Resources.S_SETTINGS_OVERRIDE_ADB,
                                  commands: [
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearAdbPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                                  ]),
                new BoolSetting(() => Settings.DisableAdbRestrictions,
                                Strings.Resources.S_SETTINGS_DISABLE_ADB_LIMITATIONS,
                                visibleProp: AbstractSetting.ExtractPropertyInfo(() => Settings.IsCredentialVaultWritable),
                                icon: new("\uE1DE"),
                                commands: [
                                    SettingsActions.Find(a => a.Name is ActionType.ResetApp),
                                ]) { CardAppearance = ControlAppearance.Danger, Info = Strings.Resources.S_SETTINGS_DISABLE_ADB_LIMITATIONS_INFO },
                new TextboxSetting(() => Settings.DefaultFolder,
                                  Strings.Resources.S_SETTINGS_DEFAULT_FOLDER,
                                  commands: [
                                      SettingsActions.Find(a => a.Name is ActionType.ChangeDefaultPath),
                                      SettingsActions.Find(a => a.Name is ActionType.ClearDefaultPath),
                                  ]),
            ], new(FluentPathGeometries.FolderBriefcase)),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_GRAPHICS,
            [
                new ComboSetting<CultureInfo>(() => Settings.UICulture,
                                 Strings.Resources.S_SETTINGS_LANGUAGE,
                                 SettingsHelper.GetAvailableLanguages(),
                                 Settings.CultureTranslationProgress,
                                 new("\uF2B7"),
                                 SettingsActions.Find(a => a.Name is ActionType.ResetApp)),
                new SimpleComboSetting<AppSettings.AppTheme>(() => Settings.Theme, Strings.Resources.S_SETTINGS_GROUP_THEME, [
                    new(AppSettings.AppTheme.Light, Strings.Resources.S_SETTINGS_THEME_LIGHT),
                    new(AppSettings.AppTheme.Dark, Strings.Resources.S_SETTINGS_THEME_DARK),
                    new(AppSettings.AppTheme.WindowsDefault, Strings.Resources.S_SETTINGS_THEME_DEFAULT)], 
                    icon: new("\uE2B1")),
                new BoolSetting(() => Settings.UseCustomAccent,
                                Strings.Resources.S_SETTINGS_CUSTOM_ACCENT,
                                icon: new("\uE790")),
                new ColorSetting(() => Settings.AccentColor,
                                       Strings.Resources.S_SETTINGS_ACCENT_COLOR,
                                       visibleProp: AbstractSetting.ExtractPropertyInfo(() => Settings.UseCustomAccent),
                                       icon: new("\uE771")),
                new BoolSetting(() => Settings.SwRender, Strings.Resources.S_SETTINGS_DISABLE_HW, icon: new("\uF211")),
            ], new("\uE2B1")),
            new SettingsGroup(Strings.Resources.S_SETTINGS_GROUP_ABOUT, BuildAboutSettings(), new("\uE946")),
        ];
    }
}

public abstract class SettingsBase : ViewModelBase
{
    public BaseAction[] Commands { get; protected set; }
}

public abstract class AbstractGroup : SettingsBase
{
    public List<AbstractSetting> Children { get; set; }
}

public class SettingsGroup : AbstractGroup
{
    public string Name { get; set; }

    public object? IconContent { get; set; }

    public SettingsGroup(string name, List<AbstractSetting> children, BaseIcon? icon = null)
    {
        Name = name;
        Children = children;
        IconContent = icon?.IconContent;
    }
}

public abstract class AbstractSetting : SettingsBase
{
    protected readonly PropertyInfo valueProp;
    protected readonly PropertyInfo visibleProp;

    public string Description { get; private set; }
    public object? IconContent { get; set; }
    public TextAlignment HeaderAlignment { get; protected set; }
    public string? Info { get; init; }

    /// <summary>
    /// Optional WPF UI appearance accent for the settings card background and border.
    /// </summary>
    public ControlAppearance? CardAppearance { get; init; }

    public Visibility Visibility
    {
        get
        {
            if (visibleProp is null)
                return Visibility.Visible;

            var value = visibleProp.GetValue(Settings);
            if (value is bool boolValue)
            {
                return boolValue ? Visibility.Visible : Visibility.Collapsed;
            }
            else if (value is Enum enumVal)
            {
                return Convert.ToInt32(enumVal) == 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            else if (value is string strVal)
            {
                return string.IsNullOrEmpty(strVal) ? Visibility.Collapsed : Visibility.Visible;
            }

            return Visibility.Visible;
        }
    }

    protected AbstractSetting(PropertyInfo valueProp, string description, PropertyInfo visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
    {
        this.visibleProp = visibleProp;
        this.valueProp = valueProp;
        Description = description;
        Commands = commands;
        IconContent = icon?.IconContent;

        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    protected virtual void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(Visibility));
        }
    }

    public static PropertyInfo ExtractPropertyInfo<T>(Expression<Func<T>> expr)
    {
        if (expr?.Body is MemberExpression member
            && member.Member is PropertyInfo info)
        {
            return info;
        }

        return null;
    }

}

public class InfoSetting : AbstractSetting
{
    public FontFamily FontFamily { get; set; }
    public int FontSize { get; set; }
    public string AltText { get; set; }

    public InfoSetting(string description, BaseIcon? icon = null, FontFamily fontFamily = null, int fontSize = 14, string altText = null, TextAlignment headerAlignment = TextAlignment.Left)
        : base(null, description, icon: icon)
    {
        FontFamily = fontFamily ?? new("Segoe UI");
        FontSize = fontSize;
        AltText = altText;
        HeaderAlignment = headerAlignment;
    }
}

public class LongDescriptionSetting : AbstractSetting
{
    public string AltText { get; set; }

    public LongDescriptionSetting(string description, string altText, BaseIcon? icon = null) 
        : base(null, description, null, icon)
    {
        AltText = altText;
    }
}

public class MultiLinkSetting : AbstractSetting
{
    public List<LinkSetting> Links { get; set; } = [];
    public MultiLinkSetting(string description, List<LinkSetting> links, BaseIcon? icon = null)
        : base(null, description, icon: icon)
    {
        Links = links;
    }
}

public class LinkSetting : AbstractSetting
{
    private readonly Func<string>? _resolveFilePath;

    public Uri Url { get; set; }
    public string AltText { get; set; }

    public BaseAction Command => new(() => true, () =>
    {
        try
        {
            if (_resolveFilePath is not null)
                Process.Start("explorer.exe", _resolveFilePath());
            else if (Url.IsFile)
                Process.Start("explorer.exe", Url.LocalPath);
            else
                Network.OpenUrl(Url.ToString(), RuntimeSettings.DefaultBrowserPath);
        }
        catch
        {
            // Broken shell association or missing path — never crash from a settings link.
        }
    });

    public string ToolTip => _resolveFilePath?.Invoke()
        ?? (Url.IsFile ? Url.LocalPath : Url.ToString());

    public LinkSetting(string description, Uri url, BaseIcon? iconBase = null, string altText = null, Func<string>? resolveFilePath = null)
        : base(null, description)
    {
        _resolveFilePath = resolveFilePath;
        Url = url;
        AltText = altText;
        IconContent = iconBase?.IconContent;
    }
}

public class NumericSetting : AbstractSetting
{
    public int Value
    {
        get => (int)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public int MinValue { get; }

    public int MaxValue { get; }

    public string Unit { get; }

    public NumericSetting(Expression<Func<int>> propertyExpr,
                          string description,
                          int minValue = int.MinValue,
                          int maxValue = int.MaxValue,
                          string unit = "",
                          PropertyInfo visibleProp = null,
                          BaseIcon? icon = null,
                          params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    {
        Unit = unit;
        MinValue = minValue;
        MaxValue = maxValue;
    }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class BoolSetting : AbstractSetting
{
    public bool Value
    {
        get => (bool)valueProp.GetValue(Settings);
        set
        {
            valueProp.SetValue(Settings, value);
            OnPropertyChanged(nameof(Label));
        }
    }

    public string Label => Value ? Strings.Resources.S_SETTINGS_ACTIVE : Strings.Resources.S_SETTINGS_INACTIVE;

    public BoolSetting(Expression<Func<bool>> propertyExpr, string description, PropertyInfo visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    { }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class TextboxSetting : AbstractSetting
{
    public string Value
    {
        get => (string)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public TextboxSetting(Expression<Func<string>> propertyExpr, string description, PropertyInfo visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    { }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class SimpleComboSetting<T> : AbstractSetting
{
    public T Value
    {
        get => (T)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public IEnumerable<EnumComboboxItem> Options { get; } = [];

    public SimpleComboSetting(Expression<Func<T>> propertyExpr, string description, IEnumerable<EnumComboboxItem> options, PropertyInfo visibleProp = null, BaseIcon? icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon, commands)
    {
        Options = options;
    }
}

public class ComboSetting<T> : AbstractSetting
{
    public T Value
    {
        get => (T)valueProp.GetValue(Settings);
        set => valueProp.SetValue(Settings, value);
    }

    public IEnumerable<T> Options { get; } = [];

    public ObservableProperty<string> ObservableAltLabel { get; } = new();

    public string AltLabel { get; private set; } = null;

    public ComboSetting(Expression<Func<T>> propertyExpr, string description, IEnumerable<T> options, ObservableProperty<string> altLabel = null, BaseIcon? icon = null, params BaseAction[] commands)
        : base(ExtractPropertyInfo(propertyExpr), description, null, icon, commands)
    {
        Options = options;
        ObservableAltLabel = altLabel;

        if (ObservableAltLabel is not null)
        {
            ObservableAltLabel.PropertyChanged += (sender, e) =>
            {
                AltLabel = e.NewValue;
                OnPropertyChanged(nameof(AltLabel));
            };

            AltLabel = ObservableAltLabel.Value;
        }
    }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp.Name)
            OnPropertyChanged(nameof(Value));
    }
}

public class ColorSetting : AbstractSetting
{
    public Color PickerColor
    {
        get => (Color)valueProp.GetValue(Settings); 
        set => valueProp.SetValue(Settings, value);
    }

    public AsyncRelayCommand PickColorCommand { get; }

    public ColorSetting(Expression<Func<Color>> propertyExpr,
                        string description,
                        PropertyInfo visibleProp = null,
                        BaseIcon? icon = null)
        : base(ExtractPropertyInfo(propertyExpr), description, visibleProp, icon)
    {
        PickColorCommand = new AsyncRelayCommand(async () =>
        {
            var panel = new Controls.ColorPicker.ColorPickerPanel
            {
                SelectedColor = PickerColor
            };

            var result = await DialogService.ShowDialog(
                AdbContentDialog.CustomContentDialog(panel),
                Strings.Resources.S_PICK_COLOR,
                primaryText: Strings.Resources.S_CONFIRM,
                closeText: Strings.Resources.S_CANCEL);

            if (result == Wpf.Ui.Controls.ContentDialogResult.Primary)
                PickerColor = panel.SelectedColor;
        });
    }

    protected override void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        base.Settings_PropertyChanged(sender, e);
        if (e.PropertyName == valueProp?.Name)
            OnPropertyChanged(nameof(PickerColor));
    }
}

public class EnumComboboxItem : ViewModelBase
{
    public Enum Key { get; set; }
    public string Name { get; set; }

    readonly PropertyInfo visibleProp;

    public bool IsEnabled
    {
        get
        {
            if (visibleProp == null)
                return true;

            object? value = visibleProp.GetValue(Settings);
            if (value is bool val)
                return val;
            if (value is string str)
                return !string.IsNullOrEmpty(str);

            return true;
        }
    }

    public EnumComboboxItem(Enum key, string name, PropertyInfo visibleProp = null)
    {
        Key = key;
        Name = name;
        this.visibleProp = visibleProp;

        Settings.PropertyChanged += Settings_PropertyChanged;
    }

    private void Settings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == visibleProp?.Name)
        {
            OnPropertyChanged(nameof(IsEnabled));
        }
    }
}

public class SettingsAction : BaseAction
{
    public enum ActionType
    {
        ChangeDefaultPath,
        ChangeAdbPath,
        ClearDefaultPath,
        ClearAdbPath,
        ResetApp,
    }

    public ActionType Name { get; }

    public string Icon { get; }

    public string Tooltip { get; }

    public SettingsAction(ActionType name, Func<bool> canExecute, Action action, string icon, string tooltip)
        : base(canExecute, action)
    {
        Name = name;
        Icon = icon;
        Tooltip = tooltip;
    }
}
