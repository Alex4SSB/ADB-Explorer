using static ADB_Explorer.Models.AdbRegEx;

namespace ADB_Explorer.Services;

public partial class AdbService
{
    public const string BATTERY = "dumpsys battery";

    private const int BATTERY_PROPERTY_CURRENT_NOW = 2;

    private static readonly string[] BATTERY_CURRENT_NOW_PATHS =
    [
        "/sys/class/power_supply/battery/current_now",
        "/sys/class/power_supply/main/current_now",
        "/sys/class/power_supply/bms/current_now",
    ];

    private static readonly int[] BATTERY_PROPERTIES_TRANSACTIONS = [1, 3];

    private enum BatteryCurrentProbeSource
    {
        Unprobed,
        Unavailable,
        Sysfs,
        BatteryProperties,
    }

    private readonly record struct BatteryCurrentProbeState(BatteryCurrentProbeSource Source, string? SysfsPath = null, int? Transaction = null);

    private static readonly ConcurrentDictionary<string, BatteryCurrentProbeState> BatteryCurrentProbeCache = new();

    public static Dictionary<string, string> GetBatteryInfo(string deviceId, CancellationToken cancellationToken)
    {
        Dictionary<string, string> info = null;
        long? currentMicroAmps = null;

        // dumpsys lacked the current when the probe was cached, so read the cached source in the same call
        if (BatteryCurrentProbeCache.TryGetValue(deviceId, out var state) && GetCurrentCommand(state) is string[] currentCommand)
        {
            ExecuteDeviceAdbShellCommand(deviceId, BATTERY, out string stdout, out _, cancellationToken, ["&&", "echo", $"{AdbExplorerConst.ADB_UNIT_SEP}", ";", .. currentCommand]);

            // No separator means dumpsys failed
            var parts = stdout.Split(AdbExplorerConst.ADB_UNIT_SEP, 2);
            if (parts.Length == 2)
            {
                info = ParseBatteryInfo(parts[0]);
                currentMicroAmps = ParseCurrent(state, parts[1]);

                // The cached source stopped working - drop it so the fallback below probes afresh
                if (currentMicroAmps is null)
                    BatteryCurrentProbeCache.TryRemove(deviceId, out _);
            }
        }
        else if (ExecuteDeviceAdbShellCommand(deviceId, BATTERY, out string stdout, out _, cancellationToken) == 0)
        {
            info = ParseBatteryInfo(stdout);
        }

        if (info is not null && TryGetDumpsysCurrentMicroAmps(info) is long dumpsysCurrent)
        {
            info["Current now"] = dumpsysCurrent.ToString();
            return info;
        }

        currentMicroAmps ??= TryGetBatteryCurrentMicroAmps(deviceId, cancellationToken);
        if (currentMicroAmps is null && info is null)
            return null;

        info ??= [];
        if (currentMicroAmps is not null)
            info["Current now"] = currentMicroAmps.Value.ToString();

        return info;
    }

    public static Dictionary<string, string> ParseBatteryInfo(string dumpsysOutput)
    {
        // Some vendors (e.g. Samsung) append log buffers with timestamped lines, producing duplicate keys.
        // Keep the first occurrence, which belongs to the standard battery service section.
        Dictionary<string, string> info = [];
        foreach (var line in dumpsysOutput.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(':', 2);
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[0]))
                continue;

            info.TryAdd(parts[0].Trim(), parts[1].Trim());
        }

        return info;
    }

    private static long? TryGetDumpsysCurrentMicroAmps(Dictionary<string, string> info)
    {
        foreach (var key in new[] { "Current now", "current now" })
        {
            if (info.TryGetValue(key, out string value) && long.TryParse(value, out long microAmps))
                return microAmps;
        }

        return null;
    }

    private static long? TryGetBatteryCurrentMicroAmps(string deviceId, CancellationToken cancellationToken)
    {
        if (BatteryCurrentProbeCache.TryGetValue(deviceId, out var state))
        {
            switch (state.Source)
            {
                case BatteryCurrentProbeSource.Unavailable:
                    return null;
                case BatteryCurrentProbeSource.Sysfs when state.SysfsPath is not null:
                    if (TryReadSysfsCurrent(deviceId, state.SysfsPath, cancellationToken) is long sysfsCurrent)
                        return sysfsCurrent;
                    BatteryCurrentProbeCache.TryRemove(deviceId, out _);
                    break;
                case BatteryCurrentProbeSource.BatteryProperties when state.Transaction is int transaction:
                    if (TryReadBatteryPropertiesCurrent(deviceId, transaction, cancellationToken) is long propertyCurrent)
                        return propertyCurrent;
                    BatteryCurrentProbeCache.TryRemove(deviceId, out _);
                    break;
            }
        }

        foreach (var path in BATTERY_CURRENT_NOW_PATHS)
        {
            if (TryReadSysfsCurrent(deviceId, path, cancellationToken) is not long microAmps)
                continue;

            BatteryCurrentProbeCache[deviceId] = new(BatteryCurrentProbeSource.Sysfs, SysfsPath: path);
            return microAmps;
        }

        foreach (var transaction in BATTERY_PROPERTIES_TRANSACTIONS)
        {
            if (TryReadBatteryPropertiesCurrent(deviceId, transaction, cancellationToken) is not long microAmps)
                continue;

            BatteryCurrentProbeCache[deviceId] = new(BatteryCurrentProbeSource.BatteryProperties, Transaction: transaction);
            return microAmps;
        }

        BatteryCurrentProbeCache[deviceId] = new(BatteryCurrentProbeSource.Unavailable);
        return null;
    }

    private static string[] SysfsCurrentCommand(string path) => ["cat", path, "2>/dev/null"];

    private static string[] BatteryPropertiesCurrentCommand(int transaction) =>
        ["service", "call", "batteryproperties", transaction.ToString(), "i32", BATTERY_PROPERTY_CURRENT_NOW.ToString()];

    private static string[]? GetCurrentCommand(BatteryCurrentProbeState state) => state.Source switch
    {
        BatteryCurrentProbeSource.Sysfs when state.SysfsPath is not null => SysfsCurrentCommand(state.SysfsPath),
        BatteryCurrentProbeSource.BatteryProperties when state.Transaction is int transaction => BatteryPropertiesCurrentCommand(transaction),
        _ => null,
    };

    private static long? ParseCurrent(BatteryCurrentProbeState state, string output) => state.Source switch
    {
        BatteryCurrentProbeSource.Sysfs => ParseSysfsCurrent(output),
        BatteryCurrentProbeSource.BatteryProperties => ParseBatteryPropertyValue(output),
        _ => null,
    };

    private static long? ParseSysfsCurrent(string output) =>
        long.TryParse(output.Trim(LINE_SEPARATORS), out long microAmps) ? microAmps : null;

    private static long? TryReadSysfsCurrent(string deviceId, string path, CancellationToken cancellationToken)
    {
        var command = SysfsCurrentCommand(path);
        if (ExecuteDeviceAdbShellCommand(deviceId, command[0], out string stdout, out _, cancellationToken, command[1..]) != 0)
            return null;

        return ParseSysfsCurrent(stdout);
    }

    private static long? TryReadBatteryPropertiesCurrent(string deviceId, int transaction, CancellationToken cancellationToken)
    {
        var command = BatteryPropertiesCurrentCommand(transaction);
        if (ExecuteDeviceAdbShellCommand(deviceId, command[0], out string stdout, out _, cancellationToken, command[1..]) != 0)
            return null;

        return ParseBatteryPropertyValue(stdout);
    }

    public static long? ParseBatteryPropertyValue(string serviceOutput)
    {
        if (string.IsNullOrWhiteSpace(serviceOutput)
            || serviceOutput.Contains("Error:", StringComparison.OrdinalIgnoreCase))
            return null;

        var words = serviceOutput.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries)
            .Where(l => l.Contains(':'))
            .SelectMany(l =>
            {
                var data = l.Split(':', 2)[1];
                return RE_SERVICE_CALL_WORD().Matches(data).Select(m => Convert.ToUInt32(m.Groups[1].Value, 16));
            })
            .ToArray();

        // Empty reply: Result: Parcel(00000000    '....')
        if (words.Length < 4 || words[1] != 0)
            return null;

        // BatteryProperty long payloads still store the value in the low 32 bits.
        long value = unchecked((int)words[3]);

        return value == long.MinValue ? null : value;
    }
}
