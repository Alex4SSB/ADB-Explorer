using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.AdbRegEx;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Services;

public partial class ADBService
{
    private static string adbPath = "";
    private static string ADB_PATH
    {
        get
        {
            if (adbPath == "")
            {
                adbPath = Settings.ManualAdbPath is string path && !string.IsNullOrEmpty(path) ? $"\"{path}\"" : "adb";
            }
            return adbPath;
        }
    }

    private const string GET_DEVICES = "devices";
    private const string ENABLE_MDNS = "ADB_MDNS_OPENSCREEN";

    // find /sdcard/.Trash-AdbExplorer/ -maxdepth 1 -mindepth 1 \( -iname "\*" ! -iname ".RecycleIndex" ! -iname ".RecycleIndex.bak" \) 2>/dev/null | wc -l
    // Exclude the recycle folder, exclude content of sub-folders, include all files (including hidden), exclude the recycle index file, discard errors, count lines
    private static readonly string[] FIND_COUNT_PARAMS_1 = { "-maxdepth", "1", "-mindepth", "1", "\\(" };
    private static readonly string[] FIND_COUNT_PARAMS_2 = { "\\)", @"2>/dev/null" };
    private static readonly string[] FIND_COUNT_PARAMS_3 = { "|", "wc", "-l" };

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

    private static void InitProcess(Process cmdProcess)
    {
        cmdProcess.StartInfo.UseShellExecute = false;
        cmdProcess.StartInfo.RedirectStandardOutput = true;
        cmdProcess.StartInfo.RedirectStandardError = true;
        cmdProcess.StartInfo.CreateNoWindow = true;
    }

    public static Process StartCommandProcess(string file, string cmd, Encoding encoding, params string[] args)
    {
        var cmdProcess = new Process();
        var arguments = $"{cmd} {string.Join(' ', args.Where(arg => !string.IsNullOrEmpty(arg)))}";

        InitProcess(cmdProcess);
        cmdProcess.StartInfo.FileName = file;
        cmdProcess.StartInfo.Arguments = arguments;
        cmdProcess.StartInfo.StandardOutputEncoding = encoding;
        cmdProcess.StartInfo.StandardErrorEncoding = encoding;
        
        if (IsMdnsEnabled)
            cmdProcess.StartInfo.EnvironmentVariables[ENABLE_MDNS] = "1";

        cmdProcess.Start();

        if (Settings.EnableLog && !RuntimeSettings.IsLogPaused)
            CommandLog.Add(new($"{file} {arguments}"));

        return cmdProcess;
    }
    public static int ExecuteCommand(
        string file, string cmd, out string stdout, out string stderr, Encoding encoding, params string[] args)
    {
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, args);

        using var stdoutTask = cmdProcess.StandardOutput.ReadToEndAsync();
        using var stderrTask = cmdProcess.StandardError.ReadToEndAsync();
        Task.WaitAll(stdoutTask, stderrTask);
        cmdProcess.WaitForExit();

        stdout = stdoutTask.Result;
        stderr = stderrTask.Result;
        return cmdProcess.ExitCode;
    }

    public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, params string[] args) =>
        ExecuteCommand(ADB_PATH, cmd, out stdout, out stderr, Encoding.UTF8, args);

    public static int ExecuteDeviceAdbCommand(string deviceSerial, string cmd, out string stdout, out string stderr, params string[] args)
    {
        return ExecuteAdbCommand("-s", out stdout, out stderr, new[] { deviceSerial, cmd }.Concat(args).ToArray());
    }

    public static IEnumerable<string> ExecuteCommandAsync(
        string file, string cmd, Encoding encoding, CancellationToken cancellationToken, params string[] args)
    {
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, args);
        
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
                stdoutLineTask = cmdProcess.StandardOutput.ReadLineAsync();
                stdoutLineTask.Wait(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                cmdProcess.Kill();
                throw;
            }
        }
        while ((stdoutLine = stdoutLineTask.Result) != null);

        string stderr = null;
        try
        {
            var stderrTask = cmdProcess.StandardError.ReadToEndAsync();
            stderrTask.Wait(cancellationToken);
            stderr = stderrTask.Result;
        }
        catch (OperationCanceledException)
        {
            cmdProcess.Kill();
            throw;
        }

        cmdProcess.WaitForExit();

        if (cmdProcess.ExitCode != 0)
        {
            throw new ProcessFailedException(cmdProcess.ExitCode, stderr);
        }
    }

    public static IEnumerable<string> ExecuteAdbCommandAsync(string cmd, CancellationToken cancellationToken, params string[] args) =>
        ExecuteCommandAsync(ADB_PATH, cmd, Encoding.UTF8, cancellationToken, args);

    public static IEnumerable<string> ExecuteDeviceAdbCommandAsync(string deviceSerial, string cmd, CancellationToken cancellationToken, params string[] args)
    {
        return ExecuteAdbCommandAsync("-s", cancellationToken, new[] { deviceSerial, cmd }.Concat(args).ToArray());
    }

    public static int ExecuteDeviceAdbShellCommand(string deviceId, string cmd, out string stdout, out string stderr, params string[] args)
    {
        return ExecuteDeviceAdbCommand(deviceId, "shell", out stdout, out stderr, new[] { cmd }.Concat(args).ToArray());
    }

    public static async Task<string> ExecuteDeviceAdbShellCommand(string deviceId, string cmd, params string[] args)
    {
        string stdout = "", stderr = "";
        var res = await Task.Run(() => ExecuteDeviceAdbShellCommand(deviceId, cmd, out stdout, out stderr, args));

        if (res == 0)
            return "";
        else
            return string.IsNullOrEmpty(stderr) ? stdout : stderr;
    }

    public static string EscapeAdbShellString(string str, char quotes = '"')
    {
        var result = string.Concat(str.Select(c =>
            c switch
            {
                var ch when ESCAPE_ADB_SHELL_CHARS.Contains(ch) => @"\" + ch,
                _ => new string(c, 1)
            }));

        return $"{quotes}{result}{quotes}";
    }

    public static string EscapeAdbString(string str) => $"\"{str}\"";

    public static IEnumerable<LogicalDevice> GetDevices()
    {
        ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, "-l");

        return RE_DEVICE_NAME.Matches(stdout).Select(
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

    public static void KillEmulator(string emulatorName) => ExecuteDeviceAdbCommand(emulatorName, "emu", out _, out _, "kill");

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
        ExecuteAdbCommand(cmd, out string stdout, out _, fullAddress, pairingCode);
        if (stdout.ToLower() is string lower
            && (lower.Contains("cannot connect") || lower.Contains("error") || lower.Contains("failed")))
        {
            throw new Exception(stdout);
        }
    }

    public static bool MmcExists(string deviceID) => GetMmcNode(deviceID).Count > 1;

    public static GroupCollection GetMmcNode(string deviceID)
    {
        // Check to see if the MMC block device (first partition) exists (MMC0 / MMC1)
        ExecuteDeviceAdbShellCommand(deviceID, "stat", out string stdout, out _, @"-c""%t,%T""", MMC_BLOCK_DEVICES[0], MMC_BLOCK_DEVICES[1]);
        // Exit code will always be 1 since we are searching for both possibilities, and only one of them can exist

        // Get major and minor nodes in hex and return
        return RE_MMC_BLOCK_DEVICE_NODE.Match(stdout).Groups;
    }

    public static string GetMmcId(string deviceID)
    {
        var matchGroups = GetMmcNode(deviceID);
        if (matchGroups.Count < 2)
            return "";

        // Get a list of all volumes (and their nodes)
        // The public flag reduces execution time significantly
        int exitCode = ExecuteDeviceAdbShellCommand(deviceID, "sm", out string stdout, out _, "list-volumes", "public");
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
        ExecuteAdbCommand("kill-server", out _, out _);

        if (restart)
            ExecuteAdbCommand("start-server", out _, out _);
    }

    public static bool KillAdbProcess()
    {
        return ExecuteCommand("taskkill", "/f", out _, out _, Encoding.UTF8, "/im", "\"adb.exe\"") == 0;
    }

    public static bool Root(Device device)
    {
        ExecuteDeviceAdbCommand(device.ID, "root", out string stdout, out _);
        return !stdout.Contains("cannot run as root");
    }

    public static bool Unroot(Device device)
    {
        ExecuteDeviceAdbCommand(device.ID, "unroot", out string stdout, out _);
        var result = stdout.Contains("restarting adbd as non root");
        DevicesObject.UpdateDeviceRoot(device.ID, result);

        return result;
    }

    public static bool WhoAmI(string deviceId)
    {
        if (ExecuteDeviceAdbShellCommand(deviceId, "whoami", out string stdout, out _) != 0)
            return false;

        return stdout.Trim() == "root";
    }

    public static ulong CountFiles(string deviceID, string path, IEnumerable<string> includeNames = null, IEnumerable<string> excludeNames = null)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, true);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, args);

        return ulong.TryParse(stdout, out var count) ? count : 0;
    }

    public static string[] FindFilesInPath(string deviceID, string path, IEnumerable<string> includeNames = null, IEnumerable<string> excludeNames = null)
    {
        string[] args = PrepFindArgs(path, includeNames, excludeNames, false);

        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, args);

        return stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    public static string[] FindFiles(string deviceID, IEnumerable<string> paths)
    {
        ExecuteDeviceAdbShellCommand(deviceID, "find", out string stdout, out _, paths.Select(item => EscapeAdbShellString(item)).Append(@"2>/dev/null").ToArray());

        return stdout.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
    }

    private static string[] PrepFindArgs(string path, IEnumerable<string> includeNames, IEnumerable<string> excludeNames, bool countOnly)
    {
        if (includeNames is not null && excludeNames is not null)
            throw new ArgumentException("Valid combinations include:\n• includeNames = null, excludeNames = null\n• includeNames != null, excludeNames = null\n• includeNames = null, excludeNames != null");

        if (!path.EndsWith('/'))
            path += "/";

        string[] args = { EscapeAdbShellString(path) };
        args = args.Concat(FIND_COUNT_PARAMS_1).ToArray();

        if (includeNames is not null)
        {
            for (int i = 0; i < includeNames.Count(); i++)
            {
                if (i > 0)
                    args = args.Append("-o").ToArray();

                args = args.Concat(new[] { "-iname", EscapeAdbShellString(includeNames.ElementAt(i)) }).ToArray();
            }
        }
        else
            args = args.Concat(new[] { "-iname", "\"\\*\"" }).ToArray();

        if (excludeNames is not null)
        {
            foreach (var item in excludeNames)
            {
                args = args.Concat(new[] { "!", "-iname", EscapeAdbShellString(item) }).ToArray();
            }
        }

        args = args.Concat(FIND_COUNT_PARAMS_2).ToArray();
        if (countOnly)
            args = args.Concat(FIND_COUNT_PARAMS_3).ToArray();

        return args;
    }

    public static long CountRecycle(string deviceID)
    {
        return (long)CountFiles(deviceID, RECYCLE_PATH, excludeNames: new[] { "*" + RECYCLE_INDEX_SUFFIX });
    }

    public static ulong CountPackages(string deviceID)
    {
        return CountFiles(deviceID, TEMP_PATH, includeNames: INSTALL_APK.Select(name => "*" + name));
    }

    public static Version VerifyAdbVersion(string adbPath)
    {
        if (string.IsNullOrEmpty(adbPath))
            return null;

        int exitCode = 1;
        string stdout = "";
        try
        {
            exitCode = ExecuteCommand(adbPath, "version", out stdout, out _, Encoding.UTF8);
        }
        catch (Exception) { }

        if (exitCode != 0)
            return null;

        string version = RE_ADB_VERSION.Match(stdout).Groups["version"]?.Value;
        return string.IsNullOrEmpty(version) ? null : new Version(version);
    }
}
