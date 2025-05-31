using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal static class SettingsHelper
{
    public static void DisableAnimationTipAction() =>
        DialogService.ShowMessage(Strings.Resources.S_DISABLE_ANIMATION, Strings.Resources.S_ANIMATION_TITLE, DialogService.DialogIcon.Tip);

    public static void AdvancedDragTipAction() =>
        DialogService.ShowMessage(Strings.Resources.S_ADVANCED_DRAG, Strings.Resources.S_ADVANCED_DRAG_TITLE, DialogService.DialogIcon.Tip);

    public static void ResetAppAction()
    {
        Process.Start(Environment.ProcessPath);
        Application.Current.Shutdown();
    }

    public static void ChangeDefaultPathAction()
    {
        var dialog = new CommonOpenFileDialog()
        {
            IsFolderPicker = true,
            Multiselect = false
        };
        if (Data.Settings.DefaultFolder != "")
            dialog.DefaultDirectory = Data.Settings.DefaultFolder;

        if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
        {
            Data.Settings.DefaultFolder = dialog.FileName;
        }
    }

    public static void ChangeAdbPathAction()
    {
        var dialog = new OpenFileDialog()
        {
            Multiselect = false,
            Title = Strings.Resources.S_OVERRIDE_ADB_BROWSE,
            Filter = $"{Strings.Resources.S_ADB_EXECUTABLE}|adb.exe",
        };

        if (!string.IsNullOrEmpty(Data.Settings.ManualAdbPath))
        {
            try
            {
                dialog.InitialDirectory = Directory.GetParent(Data.Settings.ManualAdbPath).FullName;
            }
            catch (Exception) { }
        }

        if (dialog.ShowDialog() == true)
        {
            string message = "";
            ADBService.VerifyAdbVersion(dialog.FileName);
            if (Data.RuntimeSettings.AdbVersion is null)
            {
                message = Strings.Resources.S_MISSING_ADB_OVERRIDE;
            }
            else if (Data.RuntimeSettings.AdbVersion < AdbExplorerConst.MIN_ADB_VERSION)
            {
                message = Strings.Resources.S_ADB_VERSION_LOW_OVERRIDE;
            }

            if (message != "")
            {
                DialogService.ShowMessage(message, Strings.Resources.S_FAIL_OVERRIDE_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true);
                return;
            }

            Data.Settings.ManualAdbPath = dialog.FileName;
        }
    }

    public static void SetSymbolFont()
    {
        Application.Current.Resources["SymbolThemeFontFamily"] = App.Current.FindResource(Data.RuntimeSettings.UseFluentStyles ? "FluentSymbolThemeFontFamily" : "AltSymbolThemeFontFamily");
    }

    public static async void SplashScreenTask()
    {
        var startTime = DateTime.Now;
        var versionValid = await AdbHelper.CheckAdbVersion();
        var delay = AdbExplorerConst.SPLASH_DISPLAY_TIME - (DateTime.Now - startTime);
        
        if (Data.Settings.ShowWelcomeScreen || !versionValid || !Data.Settings.AdvancedDragSet)
            return;

        await Task.Delay(Data.Settings.EnableSplash && delay > TimeSpan.Zero ? delay : TimeSpan.Zero);

        Data.RuntimeSettings.FinalizeSplash = true;
    }

    public static async void CheckAppVersions()
    {
        if (new Version(Properties.AppGlobal.AppVersion) > new Version(Data.Settings.LastVersion))
        {
            await App.Current.Dispatcher.Invoke(async () =>
            {
                var res = await DialogService.ShowConfirmation(
                    Strings.Resources.S_NEW_VERSION_MSG,
                    Strings.Resources.S_NEW_VERSION_TITLE,
                    Strings.Resources.S_GO_TO_RELEASE_NOTES,
                    cancelText: Strings.Resources.S_BUTTON_CLOSE);

                if (res.Item1 is ContentDialogResult.Primary)
                    Process.Start(Data.RuntimeSettings.DefaultBrowserPath, $"\"https://github.com/Alex4SSB/ADB-Explorer/releases/tag/v{Properties.AppGlobal.AppVersion}\"");
            });

            Data.Settings.LastVersion = Properties.AppGlobal.AppVersion;
        }

        if (Data.RuntimeSettings.IsAppDeployed || !Data.Settings.CheckForUpdates)
            return;

        var latestVersion = await Network.LatestAppReleaseAsync();
        if (latestVersion is null || latestVersion <= Data.AppVersion)
            return;

        await App.Current.Dispatcher.Invoke(async () =>
        {
            var res = await DialogService.ShowConfirmation(string.Format(Strings.Resources.S_NEW_VERSION, Properties.AppGlobal.AppDisplayName, latestVersion),
                Strings.Resources.S_NEW_VERSION_TITLE,
                Strings.Resources.S_GO_TO_VERSION_PAGE,
                cancelText: Strings.Resources.S_BUTTON_CLOSE,
                icon: DialogService.DialogIcon.Informational);

            if (res.Item1 is ContentDialogResult.Primary)
                Process.Start(Data.RuntimeSettings.DefaultBrowserPath, $"\"https://github.com/Alex4SSB/ADB-Explorer/releases/tag/v{latestVersion}\"");
        });
    }

    public static void ShowAndroidRobotLicense()
    {
        HyperlinkButton ccLink = new()
        {
            Content = Strings.Resources.S_CC_NAME,
            ToolTip = Links.L_CC_LIC,
            NavigateUri = Links.L_CC_LIC,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        HyperlinkButton apacheLink = new()
        {
            Content = "Apache",
            ToolTip = Links.L_APACHE_LIC,
            NavigateUri = Links.L_APACHE_LIC,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        apacheLink.SetValue(Grid.ColumnProperty, 1);

        SimpleStackPanel stack = new()
        {
            Spacing = 8,
            Children =
            {
                new TextBlock()
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = Strings.Resources.S_ANDROID_ROBOT_LIC,
                },
                new TextBlock()
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = Strings.Resources.S_APK_ICON_LIC,
                },
                new Grid()
                {
                    ColumnDefinitions = { new(), new() },
                    Children = { ccLink, apacheLink },
                },
            },
        };

        App.Current.Dispatcher.Invoke(() =>
        {
            DialogService.ShowDialog(stack, Strings.Resources.S_ANDROID_ICONS_TITLE, DialogService.DialogIcon.Informational);
        });
    }

    public static void ProgressMethodTipAction()
    {
        //var rtl = CultureInfo.CurrentUICulture.TextInfo.IsRightToLeft;
        // TODO: use RTL support in the future, for now just use LTR

        var text = 
        $"""
        • {Strings.Resources.S_DEPLOY_REDIRECTION_TITLE}
            {Strings.Resources.S_DEPLOY_REDIRECTION}
            {(Data.RuntimeSettings.IsArm ? Strings.Resources.S_DEPLOY_REDIRECTION_ARM : Strings.Resources.S_DEPLOY_REDIRECTION_x64)}
        
        • {Strings.Resources.S_DISK_USAGE_PROGRESS_TITLE}
            {Strings.Resources.S_DISK_USAGE_PROGRESS.Replace("\n", "\n    ")}
        """;

        DialogService.ShowMessage(text, Strings.Resources.S_PROGRESS_METHOD_TITLE, DialogService.DialogIcon.Tip);
    }
}
