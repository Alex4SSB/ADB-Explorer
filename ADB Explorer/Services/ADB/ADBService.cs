using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.AdbRegEx;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Services;

public partial class ADBService
{
    private const string GET_DEVICES = "devices";
    private const string ENABLE_MDNS = "ADB_MDNS_OPENSCREEN";

    // find /sdcard/.Trash-AdbExplorer/ -maxdepth 1 -mindepth 1 \( -iname "\*" ! -iname ".RecycleIndex" ! -iname ".RecycleIndex.bak" \) 2>/dev/null | wc -l
    // Exclude the recycle folder, exclude content of sub-folders, include all files (including hidden), exclude the recycle index file, discard errors, count lines
    private static readonly string[] FIND_COUNT_PARAMS_1 = ["-maxdepth", "1", "-mindepth", "1", "\\("];
    private static readonly string[] FIND_COUNT_PARAMS_2 = ["\\)", @"2>/dev/null"];
    private static readonly string[] FIND_COUNT_PARAMS_3 = ["|", "wc", "-l"];

    public static bool IsMdnsEnabled { get; set; }

    public class ProcessFailedException : Exception
    {
        public ProcessFailedException() { }

        public ProcessFailedException(int exitCode, string standardError) : base(standardError)
        {
            ExitCode = exitCode;
            StandardError = standardError;
        }

        public ProcessFailedException(int exitCode, string standardError, Exception inner) : base(standardError, inner)
        {
            ExitCode = exitCode;
            StandardError = standardError;
        }

        public int ExitCode { get; set; }
        public string StandardError { get; set; }
    };

    public static Process StartCommandProcess(string file, string cmd, Encoding encoding, bool redirect = true, Process cmdProcess = null, string workingDir = null, params string[] args)
    {
        cmdProcess ??= new();
        var arguments = string.Join(' ', args.Prepend(cmd).Where(arg => !string.IsNullOrEmpty(arg)));

        cmdProcess.StartInfo.UseShellExecute = false;
        
        cmdProcess.StartInfo.RedirectStandardOutput =
        cmdProcess.StartInfo.RedirectStandardError =
        cmdProcess.StartInfo.CreateNoWindow = redirect;

        cmdProcess.StartInfo.WorkingDirectory = workingDir;
        cmdProcess.StartInfo.FileName = file;
        cmdProcess.StartInfo.Arguments = arguments;

        if (redirect)
        {
            cmdProcess.StartInfo.StandardOutputEncoding = encoding;
        }

        if (IsMdnsEnabled)
            cmdProcess.StartInfo.EnvironmentVariables[ENABLE_MDNS] = "1";
        
        cmdProcess.Start();

        if (Settings.EnableLog && !RuntimeSettings.IsLogPaused)
            CommandLog.Add(new($"{file} {arguments}"));

        return cmdProcess;
    }
    public static int ExecuteCommand(
        string file, string cmd, out string stdout, out string stderr, Encoding encoding, CancellationToken cancellationToken, params string[] args)
    {
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, args: args);

        var stdoutTask = cmdProcess.StandardOutput.ReadToEndAsync();
        var stderrTask = cmdProcess.StandardError.ReadToEndAsync();
        
        var processTask = cmdProcess.WaitForExitAsync(cancellationToken);

        try
        {
            Task.WaitAll([stdoutTask, stderrTask, processTask], cancellationToken);

            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;
            return cmdProcess.ExitCode;
        }
        catch (OperationCanceledException)
        {
            processTask = null;
            stdoutTask = null;
            stderrTask = null;

            stdout = "";
            stderr = "";

            return -1;
        }
    }

    public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, CancellationToken cancellationToken, params string[] args)
    {
        var result = ExecuteCommand(RuntimeSettings.AdbPath, cmd, out stdout, out stderr, Encoding.UTF8, cancellationToken, args);
        RuntimeSettings.LastServerResponse = DateTime.Now;

        return result;
    }

    public static int ExecuteDeviceAdbCommand(string deviceSerial, string cmd, out string stdout, out string stderr, CancellationToken cancellationToken, params string[] args)
    {
        return ExecuteAdbCommand("-s", out stdout, out stderr, cancellationToken, [deviceSerial, cmd, .. args]);
    }

    public static async Task<string> ExecuteDeviceAdbCommand(string deviceId, CancellationToken cancellationToken, string cmd, params string[] args)
    {
        string stdout = "", stderr = "";
        var res = await Task.Run(() => ExecuteAdbCommand("-s", out stdout, out stderr, cancellationToken, [deviceId, cmd, .. args]), cancellationToken);

        if (res == 0)
            return "";
        else if (cancellationToken.IsCancellationRequested)
            return "Canceled";
        else
            return string.IsNullOrEmpty(stderr) ? stdout : stderr;
    }

    public static IEnumerable<string> ExecuteCommandAsync(
        string file, string cmd, Encoding encoding, CancellationToken cancellationToken, bool redirect = true, Process process = null, string workingDir = null, params string[] args)
    {
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, redirect, process, workingDir, args: args);
        cancellationToken.Register(() => ProcessHandling.KillProcess(cmdProcess));

        BlockingCollection<string> outputQueue = [];
        string stderr = "";
        cmdProcess.OutputDataReceived += (sender, e) =>
        {
                if (e.Data is null)
                {
                    outputQueue.CompleteAdding();
                }
                else
                {
                    outputQueue.Add(e.Data, cancellationToken);
                }

            RuntimeSettings.LastServerResponse = DateTime.Now;
        };
        cmdProcess.ErrorDataReceived += (sender, e) =>
        {
            if (e.Data is not null)
                stderr += e.Data;
        };

        cmdProcess.BeginOutputReadLine();
        foreach (string output in outputQueue.GetConsumingEnumerable(cancellationToken))
        {
            yield return output;
        }

        if (!redirect)
        {
            yield return null;
            yield break;
        }

        cmdProcess.WaitForExit();

        if (cmdProcess.ExitCode == 0)
            yield break;

        if (args.Length > 0
            && args[0] == "-s"
            && ExecuteDeviceAdbCommand(args[1], "get-state", out _, out string error, cancellationToken) != 0
            && error.StartsWith("error: device"))
        {
            // device is disconnected
            // return without throwing
            // command results are no longer relevant and will be cleared by DeviceListSetup()
        }
        else
        {
            if (!string.IsNullOrEmpty(stderr) && stderr[1] == '\0')
            {
                stderr = Encoding.Unicode.GetString(Encoding.UTF8.GetBytes(stderr));

                if (stderr.StartsWith("Error"))
                    stderr = $"{Strings.Resources.S_REDIRECTION} {stderr}";
            }

            throw new ProcessFailedException(cmdProcess.ExitCode, stderr.Trim());
        }
    }

    public static IEnumerable<string> ExecuteAdbCommandAsync(string cmd, CancellationToken cancellationToken, params string[] args) =>
        ExecuteCommandAsync(RuntimeSettings.AdbPath, cmd, Encoding.UTF8, cancellationToken, args: args);

    public static IEnumerable<string> ExecuteDeviceAdbCommandAsync(string deviceSerial, string cmd, CancellationToken cancellationToken, params string[] args)
    {
        return ExecuteAdbCommandAsync("-s", cancellationToken, [deviceSerial, cmd, .. args]);
    }

    public static int ExecuteDeviceAdbShellCommand(string deviceId, string cmd, out string stdout, out string stderr, CancellationToken cancellationToken, params string[] args)
    {
        var actualCmd = ShellCommands.TranslateCommand(cmd);

        return ExecuteDeviceAdbCommand(deviceId, "shell", out stdout, out stderr, cancellationToken, [actualCmd, .. args]);
    }

    /// <summary>
    /// Executes a device ADB shell command that is expected to return a value only upon failure.
    /// </summary>
    public static async Task<string> ExecuteVoidShellCommand(string deviceId, CancellationToken cancellationToken, string cmd, params string[] args)
    {
        string stdout = "", stderr = "";
        var res = await Task.Run(() => ExecuteDeviceAdbShellCommand(deviceId, cmd, out stdout, out stderr, cancellationToken, args), cancellationToken);

        if (res == 0)
            return "";
        else if (cancellationToken.IsCancellationRequested)
            return "Canceled";
        else
            return string.IsNullOrEmpty(stderr) ? stdout : stderr;
    }

    public static string EscapeAdbShellString(string str, char quotes = '"')
    {
        var result = string.Concat(str.Select(c =>
            c switch
            {
                '"' => "\\\\\\\"",
                _ when ESCAPE_ADB_SHELL_CHARS.Contains(c) => @"\" + c,
                _ => new string(c, 1)
            }));

        return $"{quotes}{result}{quotes}";
    }

    public static string EscapeAdbString(string str) => $"\"{str}\"";

    public static IEnumerable<LogicalDevice> GetDevices()
    {
        ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, CancellationToken.None, "-l");

        return RE_DEVICE_NAME().Matches(stdout).Select(LogicalDevice.New).Where(d => d);
    }

    public static void ConnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("connect", $"{host}:{port}");
    public static void ConnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("connect", fullAddress);
    public static void DisconnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("disconnect", $"{host}:{port}");
    public static void DisconnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("disconnect", fullAddress);

    public static void PairNetworkDevice(string fullAddress, string pairingCode) => NetworkDeviceOperation("pair", fullAddress, pairingCode);

    public static void KillEmulator(string emulatorName) => ExecuteDeviceAdbCommand(emulatorName, "emu", out _, out _, CancellationToken.None, "kill");

    /// <param name="cmd">connect / disconnect</param>
    /// <exception cref="ConnectionRefusedException"></exception>
    /// <exception cref="ConnectionTimeoutException"></exception>
    private static void NetworkDeviceOperation(string cmd, string fullAddress, string pairingCode = null)
    {
        ExecuteAdbCommand(cmd, out string stdout, out _, CancellationToken.None, fullAddress, pairingCode);
        if (stdout.ToLower() is string lower
            && (lower.Contains("cannot connect") || lower.Contains("error") || lower.Contains("failed")))
        {
            throw new Exception(stdout);
        }
    }

    public static bool MmcExists(string deviceID) => GetMmcNode(deviceID).Count > 1;

    public static GroupCollection GetMmcNode(string deviceID)
    {
        // Check whether the MMC block device (first partition) exists (MMC0 / MMC1)
        ExecuteDeviceAdbShellCommand(deviceID, "stat", out string stdout, out _, CancellationToken.None, @"-c""%t,%T""", MMC_BLOCK_DEVICES[0], MMC_BLOCK_DEVICES[1]);
        // Exit code will always be 1 since we are searching for both possibilities, and only one of them can exist

        // Get major and minor nodes in hex and return
        return RE_MMC_BLOCK_DEVICE_NODE().Match(stdout).Groups;
    }

    public static string GetMmcId(string deviceID)
    {
        var matchGroups = GetMmcNode(deviceID);
        if (matchGroups.Count < 2)
            return "";

        // Get a list of all volumes (and their nodes)
        // The public flag reduces execution time significantly
        int exitCode = ExecuteDeviceAdbShellCommand(deviceID, "sm", out string stdout, out _, CancellationToken.None, "list-volumes", "public");
        if (exitCode != 0)
            return "";

        var node = $"{Convert.ToInt32(matchGroups["major"].Value, 16)},{Convert.ToInt32(matchGroups["minor"].Value, 16)}";
        // Find the ID of the device with the MMC node
        var mmcVolumeId = Regex.Match(stdout, @$"{node}\smounted\s(?<id>[\w-]+)");

        return mmcVolumeId.Success ? mmcVolumeId.Groups["id"].Value : "";
    }

    public static bool CheckMDNS()
    {
        var res = ExecuteAdbCommandAsync("mdns", CancellationToken.None, "check");

        return res.First().Contains("mdns daemon version");
    }

    public static void KillAdbServer(bool restart = false)
    {
        ExecuteAdbCommand("kill-server", out _, out _, CancellationToken.None);

        if (restart)
            ExecuteAdbCommand("start-server", out _, out _, CancellationToken.None);
    }

    public static bool KillAdbProcess()
    {
        return ExecuteCommand("taskkill", "/f", out _, out _, Encoding.UTF8, CancellationToken.None, "/im", "\"adb.exe\"") == 0;
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

    public static bool Root(Device device)
    {
        ExecuteDeviceAdbCommand(device.ID, "", out string stdout, out string stderr, CancellationToken.None, RootArgs);
        if (stdout != "" || stderr != "")
            ExecuteDeviceAdbCommand(device.ID, "", out stdout, out _, CancellationToken.None, "root");

        return !stdout.Contains("cannot run as root");
    }

    public static bool Unroot(Device device)
    {
        ExecuteDeviceAdbCommand(device.ID, "", out string stdout, out string stderr, CancellationToken.None, UnrootArgs);
        if (stdout != "" || stderr != "")
            ExecuteDeviceAdbCommand(device.ID, "", out stdout, out _, CancellationToken.None, "unroot");

        var result = stdout.Contains("restarting adbd as non root");
        DevicesObject.UpdateDeviceRoot(device.ID, result);

        return result;
    }

    public static bool WhoAmI(string deviceId)
    {
        if (ExecuteDeviceAdbShellCommand(deviceId, "whoami", out string stdout, out _, CancellationToken.None) != 0)
            return false;

        return stdout.Trim() == "root";
    }

    public static ulong CountFiles(string deviceID, string path, IEnumerable<string> includeNames = null, IEnumerable<string> excludeNames = null)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, true);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, CancellationToken.None, args);

        return ulong.TryParse(stdout, out var count) ? count : 0;
    }

    public static string[] FindFilesInPath(string deviceID, string path, IEnumerable<string> includeNames = null, IEnumerable<string> excludeNames = null, bool caseSensitive = false)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, false, caseSensitive);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, CancellationToken.None, args);

        return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string[] FindFiles(string deviceID, IEnumerable<string> paths)
    {
        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, CancellationToken.None, paths.Select(item => EscapeAdbShellString(item)).Append(@"2>/dev/null").ToArray());

        return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] PrepFindArgs(string path, IEnumerable<string> includeNames, IEnumerable<string> excludeNames, bool countOnly, bool caseSensitive = false)
    {
        if (includeNames is not null && excludeNames is not null)
            throw new ArgumentException("""
                Valid combinations include:
                • includeNames = null, excludeNames = null
                • includeNames != null, excludeNames = null
                • includeNames = null, excludeNames != null
                """);

        if (!path.EndsWith('/'))
            path += "/";

        var nameArg = caseSensitive ? "-name" : "-iname";

        string[] args = [EscapeAdbShellString(path), .. FIND_COUNT_PARAMS_1];

        if (includeNames is null)
            args = [.. args, nameArg, "\"\\*\""];
        else
            args = [.. args, string.Join(" -o ", includeNames.Select(f => $"{nameArg} {EscapeAdbShellString(f)}"))];

        if (excludeNames is not null)
            args = [.. args, string.Join(' ', excludeNames.Select(f => $"! {nameArg} {EscapeAdbShellString(f)}"))];

        args = [.. args, .. FIND_COUNT_PARAMS_2];
        if (countOnly)
            args = [.. args, .. FIND_COUNT_PARAMS_3];

        return args;
    }

    public static long CountRecycle(string deviceID)
    {
        return (long)CountFiles(deviceID, RECYCLE_PATH, excludeNames: ["*" + RECYCLE_INDEX_SUFFIX]);
    }

    public static ulong CountPackages(string deviceID)
    {
        return CountFiles(deviceID, TEMP_PATH, includeNames: INSTALL_APK.Select(name => "*" + name));
    }

    static IEnumerable<string> _repoHashList = null;
    static IEnumerable<string> RepoHashList
    {
        get
        {
            _repoHashList ??= Network.GetAdbVersionListAsync().Result;

            return _repoHashList;
        }
    }

    public static void VerifyAdbVersion(string adbPath)
    {
        if (string.IsNullOrEmpty(adbPath))
        {
            RuntimeSettings.AdbVersion = new(0, 0, 0);
            return;
        }

        string trimmedPath = adbPath.Trim('"');

        // Forbid UNC paths for security reasons
        if (trimmedPath.StartsWith(@"\\"))
        {
            RuntimeSettings.AdbVersion = null;
            return;
        }

        var adbSHA = Security.CalculateWindowsFileHash(trimmedPath, true);
        if (adbSHA is not null)
        {
            bool isValidHash = AdbVersions.HashList.Contains(adbSHA);

            if (!isValidHash)
            {
                if (!RepoHashList.Contains(adbSHA))
                {
                    RuntimeSettings.AdbVersion = null;
                    return;
                }
            }
        }

        int exitCode = 1;
        string stdout = "";
        try
        {
            exitCode = ExecuteCommand(adbPath, "version", out stdout, out _, Encoding.UTF8, CancellationToken.None);
        }
        catch { }

        if (exitCode != 0)
        {
            RuntimeSettings.AdbVersion = new(0, 0, 0);
            return;
        }

        var match = RE_ADB_VERSION().Match(stdout);
        RuntimeSettings.AdbPath = match.Groups["Path"].Value.Trim();

        string version = match.Groups["version"].Value;
        if (!string.IsNullOrEmpty(version))
            RuntimeSettings.AdbVersion = new(version);
    }
}
