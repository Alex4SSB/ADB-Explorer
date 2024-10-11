using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public static class ShellCommands
{
    public const string SYS_BIN = "/system/bin";

    public enum ShellCmd
    {
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

    public static string[] Commands => Enum.GetNames(typeof(ShellCmd));

    public static Dictionary<string, Dictionary<ShellCmd, string>> DeviceCommands { get; set; } = [];

    public static bool BusyBoxExists { get; private set; }

    public static void FindCommands(string deviceID)
    {
        int returnCode = 0;

        returnCode = ADBService.ExecuteDeviceAdbShellCommand(deviceID, "busybox", out string helpResult, out _, new(), "--help");
        BusyBoxExists = returnCode == 0;

        returnCode = ADBService.ExecuteDeviceAdbShellCommand(deviceID, "echo", out string echoResult, out _, new(), "$PATH");
        if (returnCode == 127)
        {
            if (!BusyBoxExists)
                throw new Exception("echo command not found");

            if (ADBService.ExecuteDeviceAdbShellCommand(deviceID, "busybox echo", out echoResult, out _, new(), "$PATH") != 0)
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

        bool findExists = true;
        returnCode = ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                             "find",
                                                             out string findResult,
                                                             out _,
                                                             new(),
                                                             [.. Commands.Select(c => FileHelper.ConcatPaths(mainPath, c)), "2>/dev/null"]);
        if (returnCode == 127)
        {
            if (!BusyBoxExists)
                throw new Exception("find command not found");

            findExists = false;
            ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                    "busybox find",
                                                    out findResult,
                                                    out _,
                                                    new(),
                                                    [.. Commands.Select(c => FileHelper.ConcatPaths(mainPath, c)), "2>/dev/null"]);
        }

        var sysBinCmds = findResult.Split(ADBService.LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Select(FileHelper.GetFullName).ToList();
        var missingCmds = Commands.Except(sysBinCmds).ToList();

        if (!cmdPaths.Remove(SYS_BIN))
            cmdPaths.RemoveAt(0);

        foreach (var cmdPath in cmdPaths)
        {
            if (missingCmds.Count < 1)
                break;

            ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                    $"{(findExists ? "" : "busybox ")}find",
                                                    out findResult,
                                                    out _,
                                                    new(),
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
            ADBService.ExecuteDeviceAdbShellCommand(deviceID, "alias", out string aliasResult, out _, new());

            var matches = AdbRegEx.RE_GET_ALIAS().Matches(aliasResult);

            var aliases = matches.Select(m => m.Groups["Alias"].Value).Where(missingCmds.Contains);

            foreach (var item in aliases)
            {
                if (missingCmds.Remove(item))
                    sysBinCmds.Add(item);
            }
        }

        DeviceCommands.Remove(deviceID);
        Dictionary<ShellCmd, string> deviceDict = [];

        sysBinCmds.Select<string, (ShellCmd?, string)>(c => (Enum.TryParse<ShellCmd>(c, true, out var result) ? result : null, c))
                  .Where(c => c.Item1 is not null)
                  .ForEach(c => deviceDict.TryAdd(c.Item1.Value, c.Item2));

        if (missingCmds.Count > 0 && BusyBoxExists)
        {
            missingCmds.Select<string, (ShellCmd?, string)>(c => (Enum.TryParse<ShellCmd>(c, true, out var result) ? result : null, c))
                  .Where(c => c.Item1 is not null)
                  .ForEach(c => deviceDict.TryAdd(c.Item1.Value, $"busybox {c.Item2}"));
        }

        DeviceCommands.Add(deviceID, deviceDict);
    }
}
