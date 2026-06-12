using ADB_Explorer.Models;
using ADB_Explorer.Services;
using System.Net.Sockets;

namespace ADB_Explorer.Helpers;

public static class EmulatorHelper
{
    public static string? TryGetSdkPath()
    {
        var env = Environment.GetEnvironmentVariable("ANDROID_SDK_ROOT")
            ?? Environment.GetEnvironmentVariable("ANDROID_HOME");

        if (!string.IsNullOrEmpty(env) && Directory.Exists(env))
            return env;

        var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Android", "Sdk");
        return Directory.Exists(defaultPath) ? defaultPath : null;
    }

    public static string? EmulatorPath
    {
        get
        {
            if (field is null)
            {
                var sdk = TryGetSdkPath();
                if (sdk is not null)
                {
                    var path = Path.Combine(sdk, "emulator", "emulator.exe");
                    if (File.Exists(path))
                        field = path;
                }
            }

            return field;
        }
    } = null;

    public static bool IsAvailable() => EmulatorPath is not null;

    public static string[] ListAvds()
    {
        if (EmulatorPath is null)
            return [];

        var ret = ADBService.ExecuteCommand(EmulatorPath, "-list-avds", out string stdout, out _, Encoding.UTF8, CancellationToken.None);
        if (ret != 0)
            return [];

        return stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    }

    public static void LaunchAvd(string avdName)
    {
        var emulatorPath = EmulatorPath
            ?? throw new FileNotFoundException(Strings.Resources.S_EMULATOR_SDK_NOT_FOUND);

        Process.Start(new ProcessStartInfo
        {
            FileName = emulatorPath,
            Arguments = $"-avd {ADBService.EscapeAdbString(avdName)}",
            UseShellExecute = true,
        });

        Task.Run(PowerOnAfterLaunch);
    }

    public static void EnsurePoweredOn(string emulatorId)
    {
        if (emulatorId.StartsWith("emulator-", StringComparison.Ordinal)
            && int.TryParse(emulatorId.AsSpan("emulator-".Length), out int port))
        {
            TryPowerOnConsole(port);
        }

        WakeViaAdb(emulatorId);
    }

    private static void PowerOnAfterLaunch()
    {
        for (int attempt = 0; attempt < 60; attempt++)
        {
            foreach (var port in Enumerable.Range(5554, 12))
                TryPowerOnConsole(port);

            if (Data.DevicesObject.LogicalDeviceViewModels.Any(d =>
                    d.Type is DeviceType.Emulator && d.Status is DeviceStatus.Ok))
                return;

            Thread.Sleep(1000);
        }
    }

    private static void TryPowerOnConsole(int port)
    {
        try
        {
            using TcpClient client = new();
            if (!client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(250)))
                return;

            using NetworkStream stream = client.GetStream();
            stream.ReadTimeout = 500;
            stream.WriteTimeout = 500;

            using StreamReader reader = new(stream, Encoding.ASCII, false, leaveOpen: true);
            using StreamWriter writer = new(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };

            _ = reader.ReadLine();
            writer.WriteLine("power on");
        }
        catch
        {
            // Console may not be ready yet
        }
    }

    private static void WakeViaAdb(string emulatorId)
    {
        try
        {
            ADBService.ExecuteDeviceAdbShellCommand(emulatorId, "input", out _, out _, CancellationToken.None, "keyevent", "26");
            ADBService.ExecuteDeviceAdbShellCommand(emulatorId, "input", out _, out _, CancellationToken.None, "keyevent", "224");
        }
        catch
        {
            // Device may not accept shell commands yet
        }
    }

    public static string? TryGetAvdNameFromEmuOrConsole(string emulatorId)
    {
        if (TryGetAvdNameFromEmuCommand(emulatorId, out var name)
            || TryGetAvdNameFromConsole(emulatorId, out name))
            return name;

        return null;
    }

    private static bool TryGetAvdNameFromEmuCommand(string emulatorId, out string name)
    {
        name = "";
        var ret = ADBService.ExecuteDeviceAdbCommand(emulatorId, "emu", out string stdout, out _, CancellationToken.None, "avd", "name");
        if (ret != 0)
            return false;

        name = ParseAvdNameLine(stdout);
        return !string.IsNullOrEmpty(name);
    }

    private static bool TryGetAvdNameFromConsole(string emulatorId, out string name)
    {
        name = "";
        if (!emulatorId.StartsWith("emulator-", StringComparison.Ordinal)
            || !int.TryParse(emulatorId.AsSpan("emulator-".Length), out int port))
            return false;

        try
        {
            using TcpClient client = new();
            if (!client.ConnectAsync("127.0.0.1", port).Wait(TimeSpan.FromMilliseconds(500)))
                return false;

            using NetworkStream stream = client.GetStream();
            stream.ReadTimeout = 1000;
            stream.WriteTimeout = 1000;

            using StreamReader reader = new(stream, Encoding.ASCII, false, leaveOpen: true);
            using StreamWriter writer = new(stream, Encoding.ASCII) { AutoFlush = true, NewLine = "\n" };

            _ = reader.ReadLine();
            writer.WriteLine("avd name");
            name = ParseAvdNameLine(reader.ReadToEnd());
            return !string.IsNullOrEmpty(name);
        }
        catch
        {
            return false;
        }
    }

    private static string ParseAvdNameLine(string output)
    {
        foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var trimmed = line.Trim();
            if (trimmed is "OK" or "KO" || trimmed.StartsWith("KO:", StringComparison.Ordinal))
                continue;

            return trimmed;
        }

        return "";
    }
}
