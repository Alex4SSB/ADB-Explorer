using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.ViewModels;
using AdvancedSharpAdbClient;

namespace ADB_Explorer.Helpers;

internal static class AdbHelper
{
    public static Task<bool> CheckAdbVersion() => Task.Run(() =>
    {
        string adbPath = string.IsNullOrEmpty(Data.Settings.ManualAdbPath)
            ? AdbExplorerConst.ADB_PROCESS
            : Data.Settings.ManualAdbPath;

        ADBService.VerifyAdbVersion(adbPath);

        return Data.RuntimeSettings.AdbVersion >= AdbExplorerConst.MIN_ADB_VERSION;
    });

    public static void EnableMdns() => App.SafeInvoke(async () =>
    {
        ADBService.IsMdnsEnabled = Data.Settings.EnableMdns;
        if (Data.Settings.EnableMdns)
        {
            Data.MdnsService.Enable();
        }
        else if (Data.MdnsService.State is MDNS.MdnsState.Running)
        {
            var result = await DialogService.ShowConfirmation(Strings.Resources.S_DISABLE_MDNS,
                                                              Strings.Resources.S_DISABLE_MDNS_TITLE,
                                                              Strings.Resources.S_RESTART_ADB_NOW,
                                                              cancelText: Strings.Resources.S_RESTART_LATER,
                                                              icon: DialogService.DialogIcon.Informational);

            if (result.Item1 is Wpf.Ui.Controls.ContentDialogResult.Primary)
                ADBService.KillAdbServer();

            Data.MdnsService.Disable();
        }
    });

    public static string? ReadFileAsText(LogicalDeviceViewModel device, string path)
    {
        var stream = ReadFileAsStream(device, path);
        if (stream is null)
            return null;

        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }

    public static MemoryStream? ReadFileAsStream(LogicalDeviceViewModel device, string path)
    {
        MemoryStream stream = new();
        using (SyncService service = new(device.Device.DeviceData))
        {
            try
            {
                service.Pull(path, stream);
            }
            catch
            {
                return null;
            }
        }

        stream.Position = 0;
        return stream;
    }

    public static void WriteFile(LogicalDeviceViewModel device, string path, string content)
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
