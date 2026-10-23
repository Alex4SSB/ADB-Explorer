using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;

using static ADB_Explorer.Models.AdbRegEx;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Services;

public partial class AdbService
{
    public static IEnumerable<DeviceSnapshot> GetDevices(CancellationToken cancellationToken)
    {
        ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, cancellationToken, "-l");

        return RE_DEVICE_NAME().Matches(stdout).Select(DeviceSnapshot.Parse).Where(s => s);
    }

    /// <summary>
    /// The device's Wi-Fi transmit (to us) and receive link rates in Mbps; each <see langword="null"/> when unknown.
    /// Only those fields leave the device - the rest of the status holds network identifiers.
    /// </summary>
    public static (int? TxMbps, int? RxMbps) GetWifiLinkSpeeds(string deviceId)
    {
        const string cmd = "(cmd wifi status 2>/dev/null || dumpsys wifi | grep -m1 mWifiInfo) | grep -oE ', (Tx |Rx )?Link speed: -?[0-9]+Mbps' | head -n3";

        if (ExecuteDeviceAdbShellCommand(deviceId, cmd, out string stdout, out _, CancellationToken.None) != 0)
            return (null, null);

        int? link = null, tx = null, rx = null;
        foreach (Match match in RE_WIFI_LINK_SPEED().Matches(stdout))
        {
            // Android reports -1 for a rate it doesn't know
            if (!int.TryParse(match.Groups["Mbps"].Value, out var mbps) || mbps <= 0)
                continue;

            switch (match.Groups["Direction"].Value)
            {
                case "Tx ":
                    tx = mbps;
                    break;
                case "Rx ":
                    rx = mbps;
                    break;
                default:
                    link = mbps;
                    break;
            }
        }

        // Before Android 10 there's only the plain link speed - the transmit rate, and our best guess at receive.
        return (tx ?? link, rx ?? link);
    }

    /// <summary>IP transport ID → <c>ro.serialno</c>, read once per connection so it can join its device.</summary>
    private static readonly ConcurrentDictionary<string, string> RemoteSerials = new();

    public static string GetCachedRemoteSerial(string transportId) => RemoteSerials.GetValueOrDefault(transportId, "");

    /// <returns>Whether a serial was newly read. A failed read is retried on the next call.</returns>
    public static bool ResolveRemoteSerials(IReadOnlyCollection<DeviceSnapshot> snapshots, CancellationToken cancellationToken)
    {
        foreach (var stale in RemoteSerials.Keys.Where(id => snapshots.All(s => s.ID != id)))
            RemoteSerials.TryRemove(stale, out _);

        var pending = snapshots.Where(s => s.Type is DeviceType.Remote
                                           && s.Status is DeviceStatus.Ok
                                           && !RemoteSerials.ContainsKey(s.ID));

        var resolved = false;

        // Straight to the transport: routing would send this over the device's preferred connection.
        foreach (var snapshot in pending)
        {
            if (ExecuteAdbCommand("-s", out string stdout, out _, cancellationToken, snapshot.ID, "shell", GET_PROP, SERIAL_NO) == 0)
            {
                RemoteSerials[snapshot.ID] = stdout.Trim();
                resolved = true;
            }
        }

        return resolved;
    }

    public static void ConnectNetworkDevice(string fullAddress, CancellationToken cancellationToken) => NetworkDeviceOperation("connect", fullAddress, cancellationToken);

    public static void DisconnectNetworkDevice(string fullAddress, CancellationToken cancellationToken) => NetworkDeviceOperation("disconnect", fullAddress, cancellationToken);

    public static void PairNetworkDevice(string fullAddress, string pairingCode, CancellationToken cancellationToken) => NetworkDeviceOperation("pair", fullAddress, cancellationToken, pairingCode);

    public static void KillEmulator(string emulatorName) => ExecuteDeviceAdbCommand(emulatorName, "emu", out _, out _, CancellationToken.None, "kill");

    /// <param name="cmd">connect / disconnect</param>
    /// <exception cref="ConnectionRefusedException"></exception>
    /// <exception cref="ConnectionTimeoutException"></exception>
    private static void NetworkDeviceOperation(string cmd, string fullAddress, CancellationToken cancellationToken, string pairingCode = "")
    {
        ExecuteAdbCommand(cmd, out string stdout, out _, cancellationToken, fullAddress, pairingCode);
        if (stdout.ToLower() is string lower
            && (lower.Contains("cannot connect") || lower.Contains("error") || lower.Contains("failed")))
        {
            throw new Exception(stdout);
        }
    }

    const string magiskRootArgs = """
                magiskpolicy --live 'allow adbd adbd process setcurrent'
                magiskpolicy --live 'allow adbd su process dyntransition'
                magiskpolicy --live 'permissive { su }'
                resetprop ro.secure 0
                resetprop ro.adb.secure 0
                resetprop ro.force.debuggable 1
                resetprop ro.debuggable 1
                resetprop service.adb.root 1
                resetprop ctl.restart adbd
                """;

    static string[] RootArgs => ["shell", "su", "-c", EscapeAdbShellString(string.Join(" && ", magiskRootArgs.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)))];

    static string[] UnrootArgs => ["shell", "su", "-c", EscapeAdbShellString("resetprop ro.debuggable 0 && resetprop service.adb.root 0 && setprop ctl.restart adbd")];

    public static bool Root(string deviceId)
    {
        ExecuteDeviceAdbCommand(deviceId, "", out string stdout, out string stderr, CancellationToken.None, RootArgs);
        if (stdout != "" || stderr != "")
            ExecuteDeviceAdbCommand(deviceId, "", out stdout, out _, CancellationToken.None, "root");

        var success = !stdout.Contains("cannot run as root");
        if (success)
            DevicesObject.UpdateDeviceRoot(deviceId, false);

        return success;
    }

    public static bool Unroot(string deviceId)
    {
        ExecuteDeviceAdbCommand(deviceId, "", out string stdout, out string stderr, CancellationToken.None, UnrootArgs);
        if (stdout != "" || stderr != "")
            ExecuteDeviceAdbCommand(deviceId, "", out stdout, out _, CancellationToken.None, "unroot");

        var result = stdout.Contains("restarting adbd as non root");
        DevicesObject.UpdateDeviceRoot(deviceId, result);

        return result;
    }

    public static ShellIdentity? GetShellIdentity(string deviceId)
    {
        if (ExecuteDeviceAdbShellCommand(deviceId, "whoami; id", out string stdout, out _, CancellationToken.None) != 0)
            return null;

        return ShellAccessHelper.ParseShellIdentity(stdout);
    }

    public const string GET_PROP = "getprop";

    public const string ANDROID_VERSION = "ro.build.version.release";

    public const string BRAND_NAME = "ro.product.brand_device_name";

    public const string HOST_NAME = "net.hostname";

    public const string SERIAL_NO = "ro.serialno";

    public const string QEMU_BOOT_AVD_NAME = "ro.boot.qemu.avd_name";

    public const string QEMU_KERNEL_AVD_NAME = "ro.kernel.qemu.avd_name";

    public const string FEATURE_SEND_RECV_V2 = "sendrecv_v2";

    public const string FEATURE_LS_V2 = "ls_v2";

    public static HashSet<string> GetDeviceFeatures(DeviceData deviceData)
    {
        static IEnumerable<string> SplitTokens(IEnumerable<string> tokens) =>
            tokens.SelectMany(t => t.Split([' ', '\t', '\n', '\r', ','], StringSplitOptions.RemoveEmptyEntries));

        try
        {
            return [.. SplitTokens(new AdbClient().GetFeatureSet(deviceData))];
        }
        catch
        {
            return deviceData.Features?.Length > 0
                ? [.. SplitTokens(deviceData.Features)]
                : [];
        }
    }

    private static readonly Lock TransportCheckLock = new();
    private static readonly TimeSpan TransportCheckInterval = TimeSpan.FromMilliseconds(250);
    private static HashSet<string> _onlineSerials = [];
    private static long _onlineSerialsQueriedAt;

    /// <summary>
    /// Whether the adb server lists this transport as online, as asked after <paramref name="since"/> (a <see cref="Stopwatch"/> timestamp).
    /// One query answers every check made before it started, and queries are spaced out, so a burst of failures doesn't flood the adb server.
    /// </summary>
    public static bool IsTransportOnline(DeviceData deviceData, long since)
    {
        lock (TransportCheckLock)
        {
            if (_onlineSerialsQueriedAt < since)
            {
                var wait = TransportCheckInterval - Stopwatch.GetElapsedTime(_onlineSerialsQueriedAt);
                if (wait > TimeSpan.Zero)
                    Thread.Sleep(wait);

                _onlineSerialsQueriedAt = Stopwatch.GetTimestamp();
                _onlineSerials = QueryOnlineSerials();
            }

            return _onlineSerials.Contains(deviceData.Serial);
        }
    }

    // Asked of the adb server directly, since the app's device list only catches up on its next poll
    private static HashSet<string> QueryOnlineSerials()
    {
        try
        {
            return [.. new AdbClient().GetDevices().Where(d => d.State is DeviceState.Online).Select(d => d.Serial)];
        }
        catch
        {
            return [];
        }
    }

    private static readonly string[] INET_ARGS = ["-f", "inet", "addr", "show", "wlan0"];

    public static void Reboot(string deviceId, string arg)
    {
        if (ExecuteDeviceAdbCommand(deviceId, "reboot", out string stdout, out string stderr, CancellationToken.None, arg) != 0)
            throw new Exception(string.IsNullOrEmpty(stderr) ? stdout : stderr);
    }

    public static bool GetDeviceIp(DeviceViewModel device)
    {
        if (ExecuteDeviceAdbShellCommand(device.ID, "ip", out string stdout, out _, CancellationToken.None, INET_ARGS) != 0)
            return false;

        var match = RE_DEVICE_WLAN_INET().Match(stdout);
        if (!match.Success)
            return false;

        device.SetIpAddress(match.Groups["IP"].Value);

        return true;
    }

    public static bool ForceMediaScan(string deviceId)
    {
        var res = ExecuteDeviceAdbShellCommand(deviceId,
            "content",
            out _,
            out _,
            CancellationToken.None,
            "call --method scan_volume",
            "--uri content://media",
            "--arg external_primary");

        return res == 0;
    }
}
