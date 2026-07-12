using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.Services;

public enum ValidationHashMode
{
    None,
    Md5,
    Crc32,
}

public sealed class DeviceShellCommands
{
    public Dictionary<ShellCommands.ShellCmd, string> Commands { get; init; } = [];

    public bool BusyBoxExists { get; init; }

    public bool FindPrintf { get; init; } = true;

    public bool FindExists { get; init; } = true;

    public bool StatExists { get; init; } = true;

    public bool TarExists { get; init; }

    public bool UnzipExists { get; init; }

    public bool ZipExists { get; init; }

    public bool TarAppendSupported { get; init; }

    /// <summary>Device <c>tar</c> supports <c>--to-command</c> (GNU/toybox archive hash validation).</summary>
    public bool TarToCommandSupported { get; init; }

    /// <summary>Device <c>tar</c> supports <c>-O</c> / extract-to-stdout (Android toybox; hash via pipe).</summary>
    public bool TarToStdoutSupported { get; init; }

    /// <summary>IEEE CRC-32 via toybox <c>cksum -HNPL</c>.</summary>
    public bool Crc32Exists { get; init; }

    /// <summary>Shell <c>md5sum</c> or <c>busybox md5sum</c>.</summary>
    public bool Md5SumExists { get; init; }

    /// <summary>Resolved command, e.g. <c>cksum -HNPL</c>.</summary>
    public string? Crc32Command { get; init; }

    /// <summary>Resolved command, e.g. <c>md5sum</c> or <c>busybox md5sum</c>.</summary>
    public string? Md5SumCommand { get; init; }
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
        unzip,
        whoami,
        zip,
    }

    public static string[] Commands => Enum.GetNames<ShellCmd>();

    // Concurrent: written on thread-pool threads by FindCommands and read concurrently elsewhere (incl. the
    // auto-open capability-probe wait), so a plain Dictionary would risk torn reads / corruption on connect.
    public static ConcurrentDictionary<string, DeviceShellCommands> DeviceCommands { get; set; } = new();

    public static bool FindExists(string deviceId)
        => !DeviceCommands.TryGetValue(deviceId, out var commands) || commands.FindExists;

    public static bool StatExists(string deviceId)
        => !DeviceCommands.TryGetValue(deviceId, out var commands) || commands.StatExists;

    public static bool FindPrintf(string deviceId)
        => !DeviceCommands.TryGetValue(deviceId, out var commands) || commands.FindPrintf;

    public static bool BusyBoxExists(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.BusyBoxExists;

    public static bool TarExists(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.TarExists;

    public static bool UnzipExists(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.UnzipExists;

    public static bool ZipExists(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.ZipExists;

    public static bool TarAppendSupported(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.TarAppendSupported;

    public static bool TarToCommandSupported(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.TarToCommandSupported;

    public static bool TarToStdoutSupported(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.TarToStdoutSupported;

    /// <summary>Whether tar can feed member bytes to a hash tool (<c>--to-command</c> or <c>-O</c>).</summary>
    public static bool TarHashPipelineSupported(string deviceId)
        => TarToCommandSupported(deviceId) || TarToStdoutSupported(deviceId);

    public static bool Crc32Exists(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.Crc32Exists;

    public static bool Md5SumExists(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) && commands.Md5SumExists;

    /// <summary>Prefer IEEE CRC-32 (<c>cksum -HNPL</c>) for all validation; otherwise MD5 when available.</summary>
    public static ValidationHashMode GetValidationHashMode(string deviceId)
    {
        if (!DeviceCommands.TryGetValue(deviceId, out var commands))
            return ValidationHashMode.None;

        if (commands.Crc32Exists)
            return ValidationHashMode.Crc32;

        if (commands.Md5SumExists)
            return ValidationHashMode.Md5;

        return ValidationHashMode.None;
    }

    public static string? GetCrc32Command(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) ? commands.Crc32Command : null;

    public static string? GetMd5SumCommand(string deviceId)
        => DeviceCommands.TryGetValue(deviceId, out var commands) ? commands.Md5SumCommand : null;

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

        var archiveProbe = ProbeArchiveCapabilities(deviceID, busyBoxExists);
        var hashProbe = ProbeHashCommands(deviceID, busyBoxExists);

        DeviceCommands[deviceID] = new()
        {
            Commands = deviceDict,
            BusyBoxExists = busyBoxExists,
            FindPrintf = findPrintf,
            FindExists = deviceDict.ContainsKey(ShellCmd.find),
            StatExists = deviceDict.ContainsKey(ShellCmd.stat),
            TarExists = archiveProbe.TarExists,
            UnzipExists = archiveProbe.UnzipExists,
            ZipExists = archiveProbe.ZipExists,
            TarAppendSupported = archiveProbe.TarAppendSupported,
            TarToCommandSupported = archiveProbe.TarToCommandSupported,
            TarToStdoutSupported = archiveProbe.TarToStdoutSupported,
            Crc32Exists = hashProbe.Crc32Exists,
            Md5SumExists = hashProbe.Md5SumExists,
            Crc32Command = hashProbe.Crc32Command,
            Md5SumCommand = hashProbe.Md5SumCommand,
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

        var archiveProbe = ProbeArchiveCapabilities(deviceID, busyBoxExists);
        var hashProbe = ProbeHashCommands(deviceID, busyBoxExists);

        DeviceCommands[deviceID] = new()
        {
            BusyBoxExists = busyBoxExists,
            FindPrintf = findPrintf,
            FindExists = findExists,
            StatExists = statExists,
            TarExists = archiveProbe.TarExists,
            UnzipExists = archiveProbe.UnzipExists,
            ZipExists = archiveProbe.ZipExists,
            TarAppendSupported = archiveProbe.TarAppendSupported,
            TarToCommandSupported = archiveProbe.TarToCommandSupported,
            TarToStdoutSupported = archiveProbe.TarToStdoutSupported,
            Crc32Exists = hashProbe.Crc32Exists,
            Md5SumExists = hashProbe.Md5SumExists,
            Crc32Command = hashProbe.Crc32Command,
            Md5SumCommand = hashProbe.Md5SumCommand,
        };
    }

    private static HashProbeResult ProbeHashCommands(string deviceID, bool busyBoxExists)
    {
        ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                BuildHashProbeScript(busyBoxExists),
                                                out string stdout,
                                                out _,
                                                CancellationToken.None);

        return ParseHashProbeOutput(stdout, busyBoxExists);
    }

    private static readonly string CKSUM_MARK = $"{AdbExplorerConst.ADB_UNIT_SEP}CKSUM{AdbExplorerConst.ADB_UNIT_SEP}";
    private static readonly string MD5_MARK = $"{AdbExplorerConst.ADB_UNIT_SEP}MD5{AdbExplorerConst.ADB_UNIT_SEP}";

    private static string BuildHashProbeScript(bool busyBoxExists)
    {
        // Single echo + $() substitution; markers use ADB_UNIT_SEP, sections use ADB_FIELD_SEP.
        var cksum = busyBoxExists ? "busybox cksum" : "cksum";
        var md5 = busyBoxExists ? "busybox md5sum" : "md5sum";
        var fs = AdbExplorerConst.ADB_FIELD_SEP;

        return "echo " +
               CKSUM_MARK +
               "$(" + cksum + " --help 2>/dev/null)" +
               fs + MD5_MARK +
               "$(" + md5 + " --help 2>/dev/null)";
    }

    /// <summary>
    /// <c>cksum</c> (used as <c>cksum -HNPL</c> for IEEE CRC-32) and <c>md5sum</c> independently.
    /// </summary>
    internal static HashProbeResult ParseHashProbeOutput(string stdout, bool busyBoxExists = false)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return default;

        var prefix = busyBoxExists ? "busybox " : "";
        var cksumHelp = ExtractProbeSection(stdout, CKSUM_MARK);
        var md5Help = ExtractProbeSection(stdout, MD5_MARK);

        string? crcCmd = !string.IsNullOrWhiteSpace(cksumHelp) ? $"{prefix}cksum -HNPL" : null;
        string? md5Cmd = !string.IsNullOrWhiteSpace(md5Help) ? $"{prefix}md5sum" : null;

        return new(crcCmd is not null, md5Cmd is not null, crcCmd, md5Cmd);
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

    private static ArchiveProbeResult ProbeArchiveCapabilities(string deviceID, bool busyBoxExists)
    {
        ADBService.ExecuteDeviceAdbShellCommand(deviceID,
                                                BuildArchiveProbeScript(busyBoxExists),
                                                out string stdout,
                                                out _,
                                                CancellationToken.None);

        return ParseArchiveProbeOutput(stdout);
    }

    private static readonly string TAR_MARK = $"{AdbExplorerConst.ADB_UNIT_SEP}TAR{AdbExplorerConst.ADB_UNIT_SEP}";
    private static readonly string UNZIP_MARK = $"{AdbExplorerConst.ADB_UNIT_SEP}UNZIP{AdbExplorerConst.ADB_UNIT_SEP}";
    private static readonly string ZIP_MARK = $"{AdbExplorerConst.ADB_UNIT_SEP}ZIP{AdbExplorerConst.ADB_UNIT_SEP}";

    private static string BuildArchiveProbeScript(bool busyBoxExists)
    {
        // Single echo + $() substitution; markers use ADB_UNIT_SEP, sections use ADB_FIELD_SEP.
        var tar = busyBoxExists ? "busybox tar" : "tar";
        var unzip = busyBoxExists ? "busybox unzip" : "unzip";
        var zip = busyBoxExists ? "busybox zip" : "zip";
        var fs = AdbExplorerConst.ADB_FIELD_SEP;

        return "echo " +
               TAR_MARK +
               "$(" + tar + " --help 2>/dev/null)" +
               fs + UNZIP_MARK +
               "$(" + unzip + " --help 2>/dev/null)" +
               fs + ZIP_MARK +
               "$(" + zip + " --help 2>/dev/null)";
    }

    internal static ArchiveProbeResult ParseArchiveProbeOutput(string stdout)
    {
        if (string.IsNullOrWhiteSpace(stdout))
            return default;

        var tarHelp = ExtractProbeSection(stdout, TAR_MARK);
        var unzipHelp = ExtractProbeSection(stdout, UNZIP_MARK);
        var zipHelp = ExtractProbeSection(stdout, ZIP_MARK);

        var tarExists = !string.IsNullOrWhiteSpace(tarHelp);
        return new(
            tarExists,
            !string.IsNullOrWhiteSpace(unzipHelp),
            !string.IsNullOrWhiteSpace(zipHelp),
            tarExists && TarHelpSupportsAppend(tarHelp),
            tarExists && TarHelpSupportsToCommand(tarHelp),
            tarExists && TarHelpSupportsToStdout(tarHelp));
    }

    private static string ExtractProbeSection(string stdout, string label)
    {
        var start = stdout.IndexOf(label, StringComparison.Ordinal);
        if (start < 0)
            return "";

        var end = stdout.IndexOf(AdbExplorerConst.ADB_FIELD_SEP, start + label.Length);
        if (end < 0)
            end = stdout.Length;

        return stdout[(start + label.Length)..end].Trim(AdbExplorerConst.ADB_FIELD_SEP, ' ', '\r', '\n');
    }

    internal static bool TarHelpSupportsAppend(string help)
    {
        if (string.IsNullOrWhiteSpace(help))
            return false;

        if (help.Contains("--append", StringComparison.OrdinalIgnoreCase))
            return true;

        // GNU: -r, --append
        if (help.Contains("-r,", StringComparison.Ordinal))
            return true;

        return AdbRegEx.RE_TAR_APPEND_BUSYBOX().IsMatch(help)
            || AdbRegEx.RE_TAR_APPEND_TOYBOX().IsMatch(help);
    }

    internal static bool TarHelpSupportsToCommand(string help)
    {
        if (string.IsNullOrWhiteSpace(help))
            return false;

        return help.Contains("--to-command", StringComparison.OrdinalIgnoreCase)
            || help.Contains("to-command", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Android toybox: <c>O  Extract to stdout</c>; GNU: <c>-O, --to-stdout</c>.
    /// </summary>
    internal static bool TarHelpSupportsToStdout(string help)
    {
        if (string.IsNullOrWhiteSpace(help))
            return false;

        if (help.Contains("--to-stdout", StringComparison.OrdinalIgnoreCase)
            || help.Contains("to-stdout", StringComparison.OrdinalIgnoreCase))
            return true;

        // Toybox 0.8.x Android help: "O  Extract to stdout"
        if (help.Contains("Extract to stdout", StringComparison.OrdinalIgnoreCase))
            return true;

        // GNU long form: -O, --to-stdout
        return help.Contains("-O,", StringComparison.Ordinal)
            && help.Contains("stdout", StringComparison.OrdinalIgnoreCase);
    }
}

internal readonly record struct ArchiveProbeResult(
    bool TarExists,
    bool UnzipExists,
    bool ZipExists,
    bool TarAppendSupported,
    bool TarToCommandSupported,
    bool TarToStdoutSupported);

internal readonly record struct HashProbeResult(
    bool Crc32Exists,
    bool Md5SumExists,
    string? Crc32Command,
    string? Md5SumCommand);
