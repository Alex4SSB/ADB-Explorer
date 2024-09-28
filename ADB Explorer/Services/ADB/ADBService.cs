using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
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

        using var stdoutTask = cmdProcess.StandardOutput.ReadToEndAsync(cancellationToken);
        using var stderrTask = cmdProcess.StandardError.ReadToEndAsync(cancellationToken);
        
        using var processTask = cmdProcess.WaitForExitAsync(cancellationToken);

        Task.WaitAll(stdoutTask, stderrTask, processTask);

        stdout = stdoutTask.Result;
        stderr = stderrTask.Result;
        return cmdProcess.ExitCode;
    }

    public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, CancellationToken cancellationToken, params string[] args)
    {
        var result = ExecuteCommand(RuntimeSettings.AdbPath, cmd, out stdout, out stderr, Encoding.UTF8, cancellationToken, args);
        RuntimeSettings.LastServerResponse = DateTime.Now;

        return result;
    }

    public static int ExecuteDeviceAdbCommand(string deviceSerial, string cmd, out string stdout, out string stderr, CancellationToken cancellationToken, params string[] args)
    {
        var result = ExecuteAdbCommand("-s", out stdout, out stderr, cancellationToken, [deviceSerial, cmd, .. args]);
        if (stdout.TrimEnd().EndsWith($"{args.FirstOrDefault()}: not found"))
        {
            // Command not found, retrying as a busybox applet
            result = ExecuteAdbCommand("-s", out stdout, out stderr, cancellationToken, [deviceSerial, cmd, "busybox", .. args]);
        }
        return result;
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

    public static IEnumerable<string> RedirectCommandAsync(
        string file, CancellationToken cancellationToken, Process process = null, string workingDir = null, params string[] args)
    {
        if (Settings.UseProgressRedirection)
        {
            if (file[0] != '"')
                file = $"\"{file}\"";

            return ExecuteCommandAsync(ProgressRedirectionPath, file, Encoding.Unicode, cancellationToken, true, process, workingDir, args: args);
        }
        else
            return ExecuteCommandAsync(file, "", Encoding.UTF8, cancellationToken, true, process, workingDir, args: args);
    }

    public static IEnumerable<string> ExecuteCommandAsync(
        string file, string cmd, Encoding encoding, CancellationToken cancellationToken, bool redirect = true, Process process = null, string workingDir = null, params string[] args)
    {
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, redirect, process, workingDir, args: args);

        Task<string?> stdoutLineTask = null;
        string stdoutLine = null;
        do
        {
            if (stdoutLine != null)
            {
                yield return stdoutLine;
            }

            try
            {
                if (redirect)
                {
                    stdoutLineTask = cmdProcess.StandardOutput.ReadLineAsync();
                    stdoutLineTask.Wait(cancellationToken);
                }
                else
                {
                    stdoutLineTask = Task.Run(async () =>
                    {
                        await cmdProcess.WaitForExitAsync(cancellationToken);

                        return (string)null;
                    });
                }


                RuntimeSettings.LastServerResponse = DateTime.Now;
            }
            catch (OperationCanceledException)
            {
                ProcessHandling.KillProcess(cmdProcess);
                throw;
            }
        }
        while ((stdoutLine = stdoutLineTask.Result) != null);

        if (!redirect)
        {
            yield return null;
            yield break;
        }

        string stderr = null;
        try
        {
            var stderrTask = cmdProcess.StandardError.ReadToEndAsync();
            stderrTask.Wait(cancellationToken);
            stderr = stderrTask.Result;
        }
        catch (OperationCanceledException)
        {
            ProcessHandling.KillProcess(cmdProcess);
            throw;
        }

        cmdProcess.WaitForExit();

        if (cmdProcess.ExitCode != 0)
        {
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
                        stderr = Strings.S_REDIRECTION + stderr;
                }

                throw new ProcessFailedException(cmdProcess.ExitCode, stderr.Trim());
            }
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
        return ExecuteDeviceAdbCommand(deviceId, "shell", out stdout, out stderr, cancellationToken, [cmd, .. args]);
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
                var ch when ESCAPE_ADB_SHELL_CHARS.Contains(ch) => @"\" + ch,
                _ => new string(c, 1)
            }));

        return $"{quotes}{result}{quotes}";
    }

    public static string EscapeAdbString(string str) => $"\"{str}\"";

    public static IEnumerable<LogicalDevice> GetDevices()
    {
        ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, new(), "-l");

        return RE_DEVICE_NAME().Matches(stdout).Select(
            m => LogicalDevice.New(
                name: DeviceHelper.ParseDeviceName(m.Groups["model"].Value, m.Groups["device"].Value),
                id: m.Groups["id"].Value,
                status: m.Groups["status"].Value)
            ).Where(d => d);
    }

    public static void ConnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("connect", $"{host}:{port}");
    public static void ConnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("connect", fullAddress);
    public static void DisconnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("disconnect", $"{host}:{port}");
    public static void DisconnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("disconnect", fullAddress);

    public static void PairNetworkDevice(string fullAddress, string pairingCode) => NetworkDeviceOperation("pair", fullAddress, pairingCode);

    public static void KillEmulator(string emulatorName) => ExecuteDeviceAdbCommand(emulatorName, "emu", out _, out _, new(), "kill");

    /// <summary>
    /// 
    /// </summary>
    /// <param name="cmd">connect / disconnect</param>
    /// <param name="host">IP address of remote device</param>
    /// <param name="port">ADB port of remote device</param>
    /// <exception cref="ConnectionRefusedException"></exception>
    /// <exception cref="ConnectionTimeoutException"></exception>
    private static void NetworkDeviceOperation(string cmd, string fullAddress, string pairingCode = null)
    {
        ExecuteAdbCommand(cmd, out string stdout, out _, new(), fullAddress, pairingCode);
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
        ExecuteDeviceAdbShellCommand(deviceID, "stat", out string stdout, out _, new(), @"-c""%t,%T""", MMC_BLOCK_DEVICES[0], MMC_BLOCK_DEVICES[1]);
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
        int exitCode = ExecuteDeviceAdbShellCommand(deviceID, "sm", out string stdout, out _, new(), "list-volumes", "public");
        if (exitCode != 0)
            return "";

        var node = $"{Convert.ToInt32(matchGroups["major"].Value, 16)},{Convert.ToInt32(matchGroups["minor"].Value, 16)}";
        // Find the ID of the device with the MMC node
        var mmcVolumeId = Regex.Match(stdout, @$"{node}\smounted\s(?<id>[\w-]+)");

        return mmcVolumeId.Success ? mmcVolumeId.Groups["id"].Value : "";
    }

    public static bool CheckMDNS()
    {
        var res = ExecuteAdbCommandAsync("mdns", new(), "check");

        return res.First().Contains("mdns daemon version");
    }

    public static void KillAdbServer(bool restart = false)
    {
        ExecuteAdbCommand("kill-server", out _, out _, new());

        if (restart)
            ExecuteAdbCommand("start-server", out _, out _, new());
    }

    public static bool KillAdbProcess()
    {
        return ExecuteCommand("taskkill", "/f", out _, out _, Encoding.UTF8, new(), "/im", "\"adb.exe\"") == 0;
    }

    public static bool Root(Device device)
    {
        ExecuteDeviceAdbCommand(device.ID, "", out string stdout, out _, new(), Settings.RootArgs);
        return !stdout.Contains("cannot run as root");
    }

    public static bool Unroot(Device device)
    {
        ExecuteDeviceAdbCommand(device.ID, "", out string stdout, out _, new(), Settings.UnrootArgs);
        var result = stdout.Contains("restarting adbd as non root");
        DevicesObject.UpdateDeviceRoot(device.ID, result);

        return result;
    }

    public static bool WhoAmI(string deviceId)
    {
        if (ExecuteDeviceAdbShellCommand(deviceId, "whoami", out string stdout, out _, new()) != 0)
            return false;

        return stdout.Trim() == "root";
    }

    public static ulong CountFiles(string deviceID, string path, IEnumerable<string> includeNames = null, IEnumerable<string> excludeNames = null)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, true);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, new(), args);

        return ulong.TryParse(stdout, out var count) ? count : 0;
    }

    public static string[] FindFilesInPath(string deviceID, string path, IEnumerable<string> includeNames = null, IEnumerable<string> excludeNames = null)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, false);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, new(), args);

        return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string[] FindFiles(string deviceID, IEnumerable<string> paths)
    {
        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, new(), paths.Select(item => EscapeAdbShellString(item)).Append(@"2>/dev/null").ToArray());

        return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] PrepFindArgs(string path, IEnumerable<string> includeNames, IEnumerable<string> excludeNames, bool countOnly)
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

        string[] args = [EscapeAdbShellString(path), .. FIND_COUNT_PARAMS_1];

        if (includeNames is not null)
        {
            for (int i = 0; i < includeNames.Count(); i++)
            {
                if (i > 0)
                    args = [.. args, "-o"];

                args = [.. args, "-iname", EscapeAdbShellString(includeNames.ElementAt(i))];
            }
        }
        else
            args = [.. args, "-iname", "\"\\*\""];

        if (excludeNames is not null)
        {
            foreach (var item in excludeNames)
            {
                args = [.. args, "!", "-iname", EscapeAdbShellString(item)];
            }
        }

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

    public static void VerifyAdbVersion(string adbPath)
    {
        if (string.IsNullOrEmpty(adbPath))
        {
            RuntimeSettings.AdbVersion = new(0, 0, 0);
            return;
        }

        int exitCode = 1;
        string stdout = "";
        try
        {
            exitCode = ExecuteCommand(adbPath, "version", out stdout, out _, Encoding.UTF8, new());
        }
        catch (Exception) { }

        if (exitCode != 0)
        {
            RuntimeSettings.AdbVersion = new(0, 0, 0);
            return;
        }

        var match = RE_ADB_VERSION().Match(stdout);
        RuntimeSettings.AdbPath = match.Groups["Path"]?.Value.Trim();

        string version = match.Groups["version"]?.Value;
        if (!string.IsNullOrEmpty(version))
            RuntimeSettings.AdbVersion = new(version);
    }

    public static string ReadLink(string deviceID, string symLinkPath)
    {
        var result = ExecuteDeviceAdbShellCommand(deviceID, "readlink", out string stdout, out string stderr, new(), "-f", EscapeAdbShellString(symLinkPath));
        if (result != 0 || string.IsNullOrEmpty(stdout))
        {
            DialogService.ShowMessage(string.IsNullOrEmpty(stderr) ? stdout : stderr, Strings.S_FOLLOW_LINK_ERROR_TITLE, DialogService.DialogIcon.Exclamation, copyToClipboard: true);
            return null;
        }

        return stdout.Trim('\r', '\n');
    }
}
