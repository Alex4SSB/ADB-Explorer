using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal static class SettingsHelpers
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
            var version = ADBService.VerifyAdbVersion(dialog.FileName);
            if (version is null)
            {
                message = Strings.S_MISSING_ADB_OVERRIDE;
            }
            else if (version < AdbExplorerConst.MIN_ADB_VERSION)
            {
                message = Strings.S_ADB_VERSION_LOW_OVERRIDE;
            }

            if (message != "")
            {
                DialogService.ShowMessage(message, Strings.S_FAIL_OVERRIDE_TITLE, DialogService.DialogIcon.Exclamation);
                return;
            }

            Data.Settings.ManualAdbPath = dialog.FileName;
        }
    }
}
