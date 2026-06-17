using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public sealed class DeviceShellCommands
{
    public Dictionary<ShellCommands.ShellCmd, string> Commands { get; init; } = [];

    public bool BusyBoxExists { get; init; }

    public bool FindPrintf { get; init; } = true;

    public bool FindExists { get; init; } = true;

    public bool StatExists { get; init; } = true;
}

public static class ShellCommands
{
    public const string SYS_BIN = "/system/bin";

    public enum ShellCmd
    {
        am,
        cat,
        cp,
        df,
        dumpsys,
        echo,
        find,
        getprop,
        ip,
        mkdir,
        mv,
        pm,
        pwd,
        readlink,
        rm,
        stat,
        tar,
        touch,
        whoami,
    }

    public static string[] Commands => Enum.GetNames<ShellCmd>();

    public static Dictionary<string, DeviceShellCommands> DeviceCommands { get; set; } = [];

    public static bool FindExists(string deviceId)
        => !DeviceCommands.TryGetValue(deviceId, out var commands) || commands.FindExists;

    public static bool StatExists(string deviceId)
        => !DeviceCommands.TryGetValue(deviceId, out var commands) || commands.StatExists;

    public static bool FindPrintf(string deviceId)
        => !DeviceCommands.TryGetValue(deviceId, out var commands) || commands.FindPrintf;

    public static bool BusyBoxExists(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.BusyBoxExists;

    public static string TranslateCommand(string cmd)
    {
        if (Data.Settings.EnableBusyBox
            && Enum.TryParse<ShellCmd>(cmd, out var enumCmd)
            && Data.DevicesObject.Current is not null
            && DeviceCommands.TryGetValue(Data.DevicesObject.Current.ID, out var device)
            && device.Commands.TryGetValue(enumCmd, out var deviceCmd))
            return deviceCmd;

        return cmd;
    }

    public static void FindCommands(string deviceID)
    {
        if (!Data.Settings.EnableBusyBox)
        {
            ProbeShellCommands(deviceID);

            return;
        }

        int returnCode = 0;

        returnCode = ADBService.ExecuteDeviceAdbShellCommand(deviceID, "busybox", out string helpResult, out _, CancellationToken.None, "--help");
        var busyBoxExists = returnCode == 0;

        returnCode = ADBService.ExecuteDeviceAdbShellCommand(deviceID, "echo", out string echoResult, out _, CancellationToken.None, "$PATH");
        if (returnCode == 127)
        {
            if (!busyBoxExists)
                throw new Exception("echo command not found");

            if (ADBService.ExecuteDeviceAdbShellCommand(deviceID, "busybox echo", out echoResult, out _, CancellationToken.None, "$PATH") != 0)
                echoResult = null;
        }

        string mainPath;
        List<string> cmdPaths = [];
        if (string.IsNullOrEmpty(echoResult))
        {
            cmdPaths = [SYS_BIN];
            mainPath = SYS_BIN;
        }
        else
        {
            cmdPaths = [.. echoResult.TrimEnd(ADBService.LINE_SEPARATORS).Split(':')];
            mainPath = (cmdPaths.Contains(SYS_BIN) ? SYS_BIN : cmdPaths[0]);
        }

        bool sysFindAvailable = true;
        returnCode = ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                             "find",
                                                             out string findResult,
                                                             out _,
                                                             CancellationToken.None,
                                                             [.. Commands.Select(c => FileHelper.ConcatPaths(mainPath, c)), "2>/dev/null"]);
        if (returnCode == 127)
        {
            if (!busyBoxExists)
                throw new Exception("find command not found");

            sysFindAvailable = false;
            ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                    "busybox find",
                                                    out findResult,
                                                    out _,
                                                    CancellationToken.None,
                                                    [.. Commands.Select(c => FileHelper.ConcatPaths(mainPath, c)), "2>/dev/null"]);
        }

        var findPrintf = ProbeFind(deviceID, busyBoxExists).FindPrintf;

        var sysBinCmds = findResult.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Select(FileHelper.GetFullName).ToList();
        var missingCmds = Commands.Except(sysBinCmds).ToList();

        if (!cmdPaths.Remove(SYS_BIN))
            cmdPaths.RemoveAt(0);

        foreach (var cmdPath in cmdPaths)
        {
            if (missingCmds.Count < 1)
                break;

            ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                    $"{(sysFindAvailable ? "" : "busybox ")}find",
                                                    out findResult,
                                                    out _,
                                                    CancellationToken.None,
                                                    [.. missingCmds.Select(c => FileHelper.ConcatPaths(cmdPath, c)), "2>/dev/null"]);

            var newCmds = findResult.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(FileHelper.GetFullName);

            foreach (var item in newCmds)
            {
                if (missingCmds.Remove(item))
                    sysBinCmds.Add(item);
            }
        }

        if (missingCmds.Count > 0)
        {
            ADBService.ExecuteDeviceAdbShellCommand(deviceID, "alias", out string aliasResult, out _, CancellationToken.None);

            var matches = AdbRegEx.RE_GET_ALIAS().Matches(aliasResult);

            var aliases = matches.Select(m => m.Groups["Alias"].Value).Where(missingCmds.Contains);

            foreach (var item in aliases)
            {
                if (missingCmds.Remove(item))
                    sysBinCmds.Add(item);
            }
        }

        Dictionary<ShellCmd, string> deviceDict = [];

        sysBinCmds.Select<string, (ShellCmd?, string)>(c => (Enum.TryParse<ShellCmd>(c, true, out var result) ? result : null, c))
                  .Where(c => c.Item1 is not null)
                  .ForEach(c => deviceDict.TryAdd(c.Item1.Value, c.Item2));

        if (missingCmds.Count > 0 && busyBoxExists)
        {
            missingCmds.Select<string, (ShellCmd?, string)>(c => (Enum.TryParse<ShellCmd>(c, true, out var result) ? result : null, c))
                  .Where(c => c.Item1 is not null)
                  .ForEach(c => deviceDict.TryAdd(c.Item1.Value, $"busybox {c.Item2}"));
        }

        DeviceCommands[deviceID] = new()
        {
            Commands = deviceDict,
            BusyBoxExists = busyBoxExists,
            FindPrintf = findPrintf,
            FindExists = deviceDict.ContainsKey(ShellCmd.find),
            StatExists = deviceDict.ContainsKey(ShellCmd.stat),
        };
    }

    private static void ProbeShellCommands(string deviceID)
    {
        var busyBoxExists = ADBService.ExecuteDeviceAdbShellCommand(deviceID, "busybox", out _, out _, CancellationToken.None, "--help") == 0;
        var (findExists, findPrintf) = ProbeFind(deviceID, busyBoxExists);

        var statCmd = busyBoxExists ? "busybox stat" : "stat";
        var statExists = ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                                 statCmd,
                                                                 out _,
                                                                 out _,
                                                                 CancellationToken.None,
                                                                 "-c",
                                                                 "%s",
                                                                 ADBService.EscapeAdbShellString("/")) == 0;

        DeviceCommands[deviceID] = new()
        {
            BusyBoxExists = busyBoxExists,
            FindPrintf = findPrintf,
            FindExists = findExists,
            StatExists = statExists,
        };
    }

    private static (bool FindExists, bool FindPrintf) ProbeFind(string deviceID, bool busyBoxExists)
    {
        var exitCode = ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                               busyBoxExists ? "busybox find" : "find",
                                                               out var findHelp,
                                                               out _,
                                                               CancellationToken.None,
                                                               "--help");

        return (exitCode == 0, findHelp.Contains("-printf FORMAT"));
    }
}
