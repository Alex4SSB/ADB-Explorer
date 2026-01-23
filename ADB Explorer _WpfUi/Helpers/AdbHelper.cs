using ADB_Explorer.Models;
using ADB_Explorer.Services;
using AdvancedSharpAdbClient;

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

    public static void UpdateMdns()
    {
        if (Data.MdnsService.State == MDNS.MdnsState.Disabled)
        {
            Data.MdnsService.State = MDNS.MdnsState.InProgress;
            AdbHelper.MdnsCheck();
        }
        else
        {
            Data.MdnsService.State = MDNS.MdnsState.Disabled;
        }

        UpdateQrClass();
    }

    private static void UpdateQrClass() => Data.RuntimeSettings.RefreshQrImage = true;

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

                if (result.Item1 is Wpf.Ui.Controls.ContentDialogResult.Primary)
                    ADBService.KillAdbServer();
            }

            Data.QrClass = null;
            Data.MdnsService.State = MDNS.MdnsState.Disabled;
        }
    });

    public static string ReadFile(ADBService.AdbDevice device, string path)
    {
        using MemoryStream stream = new();
        using SyncService service = new(device.Device.DeviceData);

        service.Pull(path, stream);

        stream.Position = 0;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static void WriteFile(ADBService.AdbDevice device, string path, string content)
    {
        using MemoryStream stream = new();
        using StreamWriter writer = new(stream);
        writer.Write(content);
        writer.Flush();
        stream.Position = 0;

        using SyncService service = new(device.Device.DeviceData);
        service.Push(stream, path, (UnixFileMode)0x1ED, DateTime.Now); // 0x1ED = 0777 in octal
    }
}
