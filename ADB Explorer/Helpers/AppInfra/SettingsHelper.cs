using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal static class SettingsHelper
{
    public static void DisableAnimationTipAction() =>
        DialogService.ShowMessage(Strings.S_DISABLE_ANIMATION, Strings.S_ANIMATION_TITLE, DialogService.DialogIcon.Tip);


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
            Title = Strings.S_OVERRIDE_ADB_BROWSE,
            Filter = "ADB Executable|adb.exe",
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
                message = Strings.S_MISSING_ADB_OVERRIDE;
            }
            else if (Data.RuntimeSettings.AdbVersion < AdbExplorerConst.MIN_ADB_VERSION)
            {
                message = Strings.S_ADB_VERSION_LOW_OVERRIDE;
            }

            if (message != "")
            {
                DialogService.ShowMessage(message, Strings.S_FAIL_OVERRIDE_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true);
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
        if (Data.Settings.ShowWelcomeScreen)
            return;

        await Task.Delay(Data.Settings.EnableSplash ? AdbExplorerConst.SPLASH_DISPLAY_TIME : TimeSpan.Zero);

        App.Current.Dispatcher.Invoke(() => Data.RuntimeSettings.IsSplashScreenVisible = false);
    }

    public static async void CheckForUpdates()
    {
        if (Data.RuntimeSettings.IsAppDeployed || !Data.Settings.CheckForUpdates)
            return;

        var version = await Network.LatestAppReleaseAsync();
        if (version is null || version <= Data.AppVersion)
            return;

        App.Current.Dispatcher.Invoke(() => DialogService.ShowMessage(Strings.S_NEW_VERSION(version), Strings.S_NEW_VERSION_TITLE, DialogService.DialogIcon.Informational));
    }

    public static void ShowAndroidRobotLicense()
    {
        HyperlinkButton ccLink = new()
        {
            Content = Strings.S_CC_NAME,
            ToolTip = Links.L_CC_LIC,
            NavigateUri = Links.L_CC_LIC,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        HyperlinkButton apacheLink = new()
        {
            Content = Strings.S_APACHE_NAME,
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
                    Text = Strings.S_ANDROID_ROBOT_LIC,
                },
                new TextBlock()
                {
                    TextWrapping = TextWrapping.Wrap,
                    Text = Strings.S_APK_ICON_LIC,
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
            DialogService.ShowDialog(stack, Strings.S_ANDROID_ICONS_TITLE, DialogService.DialogIcon.Informational);
        });
    }

    public static void ProgressMethodTipAction() =>
        DialogService.ShowMessage(Strings.S_PROGRESS_METHOD_INFO(), Strings.S_PROGRESS_METHOD_TITLE, DialogService.DialogIcon.Tip);
}
