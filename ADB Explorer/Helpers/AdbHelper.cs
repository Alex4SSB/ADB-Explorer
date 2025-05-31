using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.Helpers;

internal static class AdbHelper
{
    public static Task<bool> CheckAdbVersion() => Task.Run(() =>
    {
        string adbPath = string.IsNullOrEmpty(Data.Settings.ManualAdbPath)
            ? AdbExplorerConst.ADB_PROCESS
            : $"\"{Data.Settings.ManualAdbPath}\"";

        ADBService.VerifyAdbVersion(adbPath);

        return Data.RuntimeSettings.AdbVersion >= AdbExplorerConst.MIN_ADB_VERSION;
    });

    public static void VerifyProgressRedirection() => Task.Run(() =>
    {
        if (!Data.Settings.UseProgressRedirection
            || Data.RuntimeSettings.AdbVersion is null
            || string.IsNullOrEmpty(Data.RuntimeSettings.AdbPath))
            return;
        
        string path = $"{Data.AppDataPath}\\{AdbExplorerConst.PROGRESS_REDIRECTION_PATH}";
        bool hashValid;

        if (Data.RuntimeSettings.IsArm)
            hashValid = Security.CalculateWindowsFileHash(path) == Properties.AppGlobal.ProgressRedirectionHash_ARM;
        else
            hashValid = Security.CalculateWindowsFileHash(path) == Properties.AppGlobal.ProgressRedirectionHash_x64;

        if (!hashValid)
        {
            // hash will be null if file does not exist, or is inaccessible
            try
            {
                if (Data.RuntimeSettings.IsArm)
                    File.WriteAllBytes(path, Properties.AppGlobal.AdbProgressRedirection_ARM);
                else
                    File.WriteAllBytes(path, Properties.AppGlobal.AdbProgressRedirection_x86);
            }
            catch (Exception e)
            {
                Data.Settings.UseProgressRedirection = false;

                App.Current.Dispatcher.Invoke(() =>
                    DialogService.ShowMessage($"{Strings.Resources.S_DEPLOY_REDIRECTION_ERROR}\n\n{e.Message}",
                                              Strings.Resources.S_DEPLOY_REDIRECTION_TITLE,
                                              DialogService.DialogIcon.Exclamation,
                                              copyToClipboard: true));

                return;
            }
        }

        Data.ProgressRedirectionPath = path;
    });

    public static void MdnsCheck()
    {
        Task.Run(() => Data.MdnsService.State = ADBService.CheckMDNS() ? MDNS.MdnsState.Running : MDNS.MdnsState.NotRunning);
        Task.Run(async () =>
        {
            while (Data.MdnsService.State is MDNS.MdnsState.InProgress)
            {
                App.Current.Dispatcher.Invoke(() => Data.MdnsService.UpdateProgress());

                await Task.Delay(AdbExplorerConst.MDNS_STATUS_UPDATE_INTERVAL);
            }
        });
    }

    public static void EnableMdns() => App.Current.Dispatcher.Invoke(async () =>
    {
        ADBService.IsMdnsEnabled = Data.Settings.EnableMdns;
        if (Data.Settings.EnableMdns)
        {
            Data.QrClass = new();
        }
        else
        {
            if (Data.MdnsService.State is MDNS.MdnsState.Running)
            {
                var result = await DialogService.ShowConfirmation(Strings.Resources.S_DISABLE_MDNS,
                                                                  Strings.Resources.S_DISABLE_MDNS_TITLE,
                                                                  Strings.Resources.S_RESTART_ADB_NOW,
                                                                  cancelText: Strings.Resources.S_RESTART_LATER,
                                                                  icon: DialogService.DialogIcon.Informational);

                if (result.Item1 is ContentDialogResult.Primary)
                    ADBService.KillAdbServer();
            }

            Data.QrClass = null;
            Data.MdnsService.State = MDNS.MdnsState.Disabled;
        }
    });

    public static bool SilentPull(Device device, FilePath file, string windowsPath) 
        => 0 == ADBService.ExecuteAdbCommand("pull",
                                             out _,
                                             out _,
                                             new(),
                                             [
                                                 "-a",
                                                 ADBService.EscapeAdbString(file.FullPath),
                                                 ADBService.EscapeAdbString(windowsPath)
                                             ]);

    public static bool SilentPush(Device device, string windowsPath, string androidPath)
        => 0 == ADBService.ExecuteAdbCommand("push",
            out _,
            out _,
            new(),
            [
                ADBService.EscapeAdbString(windowsPath),
                ADBService.EscapeAdbString(androidPath)
            ]);
}
