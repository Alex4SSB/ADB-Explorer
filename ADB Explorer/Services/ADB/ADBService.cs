using ADB_Explorer.Converters;
using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.ViewModels;
using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using static ADB_Explorer.Models.AbstractFile;
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

    private static int _activeCommandCount = 0;
    public static bool IsCommandActive => _activeCommandCount > 0;

    private static readonly ConcurrentDictionary<int, Process> ActiveCommandProcesses = new();

    public static event Action<bool> CommandActiveChanged;

    private static void UpdateCommandActive(int delta)
    {
        var prev = _activeCommandCount;
        var next = Interlocked.Add(ref _activeCommandCount, delta);
        if ((prev == 0) != (next == 0))
            CommandActiveChanged?.Invoke(next > 0);
    }

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

        if (Settings.EnableLog && !IsLogPaused)
            CommandLog.Add(new($"{file} {arguments}"));

        return cmdProcess;
    }
    public static int ExecuteCommand(
        string file, string cmd, out string stdout, out string stderr, Encoding encoding, CancellationToken cancellationToken, params string[] args)
    {
        UpdateCommandActive(1);
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, args: args);
        ActiveCommandProcesses[cmdProcess.Id] = cmdProcess;

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
            KillTrackedProcess(cmdProcess);

            processTask = null;
            stdoutTask = null;
            stderrTask = null;

            stdout = "";
            stderr = "";

            return -1;
        }
        finally
        {
            ActiveCommandProcesses.TryRemove(cmdProcess.Id, out _);
            UpdateCommandActive(-1);
        }
    }

    public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, CancellationToken cancellationToken, params string[] args)
    {
        var result = ExecuteCommand(AdbHelper.CurrentAdbState.Path, cmd, out stdout, out stderr, Encoding.UTF8, cancellationToken, args);
        DiskUsagePollingService.LastServerResponse = DateTime.Now;

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
        UpdateCommandActive(1);
        var processId = -1;
        try
        {
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, redirect, process, workingDir, args: args);
        processId = cmdProcess.Id;
        ActiveCommandProcesses[processId] = cmdProcess;
        cancellationToken.Register(() => KillTrackedProcess(cmdProcess));

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

            DiskUsagePollingService.LastServerResponse = DateTime.Now;
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
            }

            throw new ProcessFailedException(cmdProcess.ExitCode, stderr.Trim());
        }
        }
        finally
        {
            if (processId >= 0)
                ActiveCommandProcesses.TryRemove(processId, out _);
            UpdateCommandActive(-1);
        }
    }

    public static IEnumerable<string> ExecuteAdbCommandAsync(string cmd, CancellationToken cancellationToken, params string[] args) =>
        ExecuteCommandAsync(AdbHelper.CurrentAdbState.Path, cmd, Encoding.UTF8, cancellationToken, args: args);

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

    public static IEnumerable<DeviceSnapshot> GetDevices(CancellationToken cancellationToken)
    {
        ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, cancellationToken, "-l");

        return RE_DEVICE_NAME().Matches(stdout).Select(DeviceSnapshot.Parse).Where(s => s);
    }

    public static void ConnectNetworkDevice(string fullAddress, CancellationToken cancellationToken) => NetworkDeviceOperation("connect", fullAddress, cancellationToken);
    public static void DisconnectNetworkDevice(string fullAddress, CancellationToken cancellationToken) => NetworkDeviceOperation("disconnect", fullAddress, cancellationToken);

    public static void PairNetworkDevice(string fullAddress, string pairingCode, CancellationToken cancellationToken) => NetworkDeviceOperation("pair", fullAddress, cancellationToken, pairingCode);

    public static void KillEmulator(string emulatorName) => ExecuteDeviceAdbCommand(emulatorName, "emu", out _, out _, CancellationToken.None, "kill");

    /// <param name="cmd">connect / disconnect</param>
    /// <exception cref="ConnectionRefusedException"></exception>
    /// <exception cref="ConnectionTimeoutException"></exception>
    private static void NetworkDeviceOperation(string cmd, string fullAddress, CancellationToken cancellationToken, string pairingCode = null)
    {
        ExecuteAdbCommand(cmd, out string stdout, out _, cancellationToken, fullAddress, pairingCode);
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
        var exitCode = ExecuteAdbCommand("mdns", out string stdout, out _, CancellationToken.None, "check");

        return exitCode == 0 && stdout.Contains("mdns daemon version");
    }

    public static void KillAdbServer(bool restart = false)
    {
        try
        {
            ExecuteAdbCommand("kill-server", out _, out _, CancellationToken.None);

            if (restart)
                ExecuteAdbCommand("start-server", out _, out _, CancellationToken.None);
        }
        catch (Win32Exception)
        {
            AdbHelper.EnterAdbSetupMode();
        }
    }

    public static void WaitForCommands(TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (IsCommandActive && DateTime.UtcNow < deadline)
            Thread.Sleep(50);
    }

    public static void CancelAllCommands()
    {
        foreach (var pair in ActiveCommandProcesses.ToArray())
        {
            KillTrackedProcess(pair.Value);
        }
    }

    private static void KillTrackedProcess(Process process)
    {
        try
        {
            if (process is { HasExited: false })
                ProcessHandling.KillProcess(process);
        }
        catch
        {
        }
    }

    public static bool KillAdbProcess()
    {
        KillAllAdbProcesses();
        return Process.GetProcessesByName(ADB_PROCESS).Length == 0;
    }

    public static void KillAllAdbProcesses()
    {
        try
        {
            KillAdbServer();
        }
        catch
        {
            // Server may already be gone or ADB path may be invalid.
        }

        for (var attempt = 0; attempt < 3; attempt++)
        {
            ExecuteCommand("taskkill", "/f", out _, out _, Encoding.UTF8, CancellationToken.None, "/im", $"{ADB_PROCESS}.exe");

            foreach (var process in Process.GetProcessesByName(ADB_PROCESS))
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill(entireProcessTree: true);
                }
                catch
                {
                }
                finally
                {
                    process.Dispose();
                }
            }

            if (Process.GetProcessesByName(ADB_PROCESS).Length == 0)
                return;

            Thread.Sleep(100);
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

        return !stdout.Contains("cannot run as root");
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

    /// <summary>
    /// Determines which of the specified file paths exist on a device identified by the given device ID.
    /// </summary>
    /// <remarks>This method executes a shell command on the device to check for the existence of the provided
    /// paths. Any errors encountered during command execution are ignored.</remarks>
    /// <param name="deviceID">The unique identifier of the device on which to check the existence of the file paths.</param>
    /// <param name="paths">An enumerable collection of file paths to verify for existence on the specified device.</param>
    /// <returns>An array of strings containing the paths that exist on the device. The array is empty if none of the specified
    /// paths exist.</returns>
    public static string[] PathsExist(string deviceID, params IEnumerable<string> paths)
    {
        ExecuteDeviceAdbShellCommand(deviceID,
                                     "find",
                                     out string stdout,
                                     out _,
                                     CancellationToken.None,
                                     [.. paths.Select(item => EscapeAdbShellString(item)), "-maxdepth 0", @"2>/dev/null"]);

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

    static IEnumerable<string> _repoHashList;
    static IEnumerable<string> RepoHashList
    {
        get
        {
            if (_repoHashList is not null)
                return _repoHashList;

            try
            {
                _repoHashList = Network.GetAdbVersionListAsync().GetAwaiter().GetResult() ?? [];
            }
            catch
            {
                _repoHashList = [];
            }

            return _repoHashList;
        }
    }

    private static readonly TimeSpan AdbVersionCheckTimeout = TimeSpan.FromSeconds(15);

    public static void VerifyAdbVersion(string adbPath)
    {
        if (string.IsNullOrEmpty(adbPath))
        {
            AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.NotFound;
            return;
        }

        // Forbid UNC paths for security reasons
        if (!Settings.DisableAdbRestrictionsActive && adbPath.StartsWith(@"\\"))
        {
            AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.PathInvalid;
            return;
        }

        FileInfo file = new(adbPath);

        if (file.Exists)
        {
            // Forbid symlinks for security reasons
            if (!Settings.DisableAdbRestrictionsActive && file.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.PathInvalid;
                return;
            }
        }
        else
        {
            // If the path is not a direct file reference, try to resolve it from the system PATH
            adbPath = FileHelper.ResolveExecutableFromPath(adbPath);
            if (adbPath is null)
            {
                AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.NotFound;
                return;
            }
        }

        if (!Settings.DisableAdbRestrictionsActive)
        {
            bool isHashValid = false;
            var adbSHA = Security.CalculateWindowsFileHash(adbPath, true);
            if (adbSHA is not null)
            {
                // First check against the hardcoded list of known ADB versions
                isHashValid = AdbVersions.HashList.Contains(adbSHA);

                // If not found, verify the certificate is valid and is from Google. ADB is signed since 34.0.5
                if (!isHashValid)
                    isHashValid = Security.VerifyAuthenticode(adbPath, "Google LLC");

                // As a last resort, check against the list retrieved from the repository (which is updated more frequently)
                if (!isHashValid)
                    isHashValid = RepoHashList.Contains(adbSHA);
            }

            if (!isHashValid)
            {
                AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.Compromised;
                return;
            }
        }
        
        int exitCode = 1;
        string stdout = "";
        try
        {
            using var cts = new CancellationTokenSource(AdbVersionCheckTimeout);
            exitCode = ExecuteCommand($"\"{adbPath}\"", "version", out stdout, out _, Encoding.UTF8, cts.Token);
        }
        catch (OperationCanceledException) { }
        catch { }

        if (exitCode != 0)
        {
            AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.VersionUnknown;
            return;
        }

        // Update the path in case we got it from environment PATH
        var match = RE_ADB_VERSION().Match(stdout);
        if (!match.Success)
        {
            AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.VersionUnknown;
            return;
        }

        AdbHelper.CurrentAdbState.Path = match.Groups["Path"].Value.Trim();

        string version = match.Groups["version"].Value;
        if (!string.IsNullOrEmpty(version) && Version.TryParse(version, out Version parsedVersion))
        {
            AdbHelper.CurrentAdbState.Version = parsedVersion;
            AdbHelper.CurrentAdbState.Status = AdbHelper.CurrentAdbState.Version < MIN_ADB_VERSION
                ? AdbHelper.AdbStatus.Outdated
                : AdbHelper.AdbStatus.Valid;
        }
        else
            AdbHelper.CurrentAdbState.Status = AdbHelper.AdbStatus.VersionUnknown;
    }

    #region Former AdbDevice members

    public static readonly char[] LINE_SEPARATORS = ['\n', '\r'];

    public const string GET_PROP = "getprop";
    public const string ANDROID_VERSION = "ro.build.version.release";
    public const string BATTERY = "dumpsys battery";
    public const string MMC_PROP = "vold.microsd.uuid";
    public const string OTG_PROP = "vold.otgstorage.uuid";

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

    // First partition of MMC block device 0 / 1
    private static readonly string[] MMC_BLOCK_DEVICES = ["/dev/block/mmcblk0p1", "/dev/block/mmcblk1p1"];
    private static readonly string[] EMULATED_DRIVES_GREP = ["|", "grep", "-E", "'/mnt/media_rw/|/storage/'"];

    private static readonly string[] STAT_LINKMODE_ARGS = ["-c", $"%n{ADB_FIELD_SEP}%f", "2>&1"];

    private static readonly string[] INET_ARGS = ["-f", "inet", "addr", "show", "wlan0"];

    private const string CURRENT_DIR = ".";
    private const string PARENT_DIR = "..";
    private static readonly string[] SPECIAL_DIRS = [CURRENT_DIR, PARENT_DIR];

    /// <summary>Represents the Unix filesystem permissions and file type.<br />
    /// Since the file type flags overlap, they CANNOT be used as flags.</summary>
    public enum UnixFileMode
    {
        /// <summary>No permissions.</summary>
        None = 0x0,
        /// <summary>Execute permission for others.</summary>
        OtherExecute = 0x1,
        /// <summary>Write permission for others.</summary>
        OtherWrite = 0x2,
        /// <summary>Read permission for others.</summary>
        OtherRead = 0x4,
        /// <summary>Execute permission for group.</summary>
        GroupExecute = 0x8,
        /// <summary>Write permission for group.</summary>
        GroupWrite = 0x10,
        /// <summary>Read permission for group.</summary>
        GroupRead = 0x20,
        /// <summary>Execute permission for owner.</summary>
        UserExecute = 0x40,
        /// <summary>Write permission for owner.</summary>
        UserWrite = 0x80,
        /// <summary>Read permission for owner.</summary>
        UserRead = 0x100,
        /// <summary>Sticky bit permission.</summary>
        StickyBit = 0x200,
        /// <summary>Set group permission.</summary>
        SetGroup = 0x400,
        /// <summary>Set user permission.</summary>
        SetUser = 0x800,
        /// <summary>FIFO.</summary>
        S_IFIFO = 0x1000,
        /// <summary>Character device.</summary>
        S_IFCHR = 0x2000,
        /// <summary>Directory.</summary>
        S_IFDIR = 0x4000,
        /// <summary>Block device.</summary>
        S_IFBLK = 0x6000,
        /// <summary>Regular file.</summary>
        S_IFREG = 0x8000,
        /// <summary>Symbolic link.</summary>
        S_IFLNK = 0xA000,
        /// <summary>Socket.</summary>
        S_IFSOCK = 0xC000,
    }

    private static FileStat? CreateFile(string path, string stdoutLine)
    {
        var match = RE_LS_FILE_ENTRY().Match(stdoutLine);
        if (!match.Success)
        {
            throw new Exception($"Invalid output for adb ls command: {stdoutLine}");
        }

        var name = match.Groups["Name"].Value;
        long? size = long.Parse(match.Groups["Size"].Value, NumberStyles.HexNumber);
        var time = long.Parse(match.Groups["Time"].Value, NumberStyles.HexNumber);
        var mode = (UnixFileMode)UInt32.Parse(match.Groups["Mode"].Value, NumberStyles.HexNumber);

        if (SPECIAL_DIRS.Contains(name))
            return null;

        var type = ParseFileMode(mode);
        if (mode is UnixFileMode.None || type is FileType.Folder)
        {
            size = null;
        }

        UnixFileMode? permissions = mode is UnixFileMode.None 
            ? null 
            : mode & (UnixFileMode)511;

        return new(
            FullName: name,
            FullPath: FileHelper.ConcatPaths(path, name),
            Type: type,
            IsLink: mode.HasFlag(UnixFileMode.S_IFLNK),
            Size: size,
            ModifiedTime: (time > 0) ? DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime() : null,
            Permissions: (System.IO.UnixFileMode?)permissions);
    }

    private static FileType ParseFileMode(UnixFileMode mode)
    {
        if (mode.HasFlag(UnixFileMode.S_IFSOCK)) return FileType.Socket;
        if (mode.HasFlag(UnixFileMode.S_IFLNK)) return FileType.Unknown;
        if (mode.HasFlag(UnixFileMode.S_IFREG)) return FileType.File;
        if (mode.HasFlag(UnixFileMode.S_IFBLK)) return FileType.BlockDevice;
        if (mode.HasFlag(UnixFileMode.S_IFDIR)) return FileType.Folder;
        if (mode.HasFlag(UnixFileMode.S_IFCHR)) return FileType.CharDevice;
        if (mode.HasFlag(UnixFileMode.S_IFIFO)) return FileType.FIFO;

        return FileType.Unknown;
    }

    public static FileExtraInfo? GetFileExtraInfo(string deviceId, string path, CancellationToken cancellationToken)
    {
        // Get user, group, access time, creation time and modification time using human-readable format to preserve UTC offset
        var res = ExecuteDeviceAdbShellCommand(deviceId, "stat", out string stdout, out _, cancellationToken, "-c", $"%U{ADB_FIELD_SEP}%G{ADB_FIELD_SEP}%x{ADB_FIELD_SEP}%z{ADB_FIELD_SEP}%y", EscapeAdbShellString(path));
        if (res != 0 || string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        string user, group;
        DateTimeOffset accessTime, creationTime, modifiedTime;

        try
        {
            var parts = stdout.Split(ADB_FIELD_SEP);
            user = parts[0];
            group = parts[1];
            accessTime = DateTimeOffset.Parse(parts[2].Trim(), CultureInfo.InvariantCulture);
            creationTime = DateTimeOffset.Parse(parts[3].Trim(), CultureInfo.InvariantCulture);
            modifiedTime = DateTimeOffset.Parse(parts[4].Trim(), CultureInfo.InvariantCulture);
        }
        catch
        {
            return null;
        }

        return new FileExtraInfo(user, group, accessTime, creationTime, modifiedTime);
    }

    public static IEnumerable<(string, FileType)> GetLinkType(string deviceId, IEnumerable<string> filePaths, CancellationToken cancellationToken)
    {
        var pathList = filePaths as IList<string> ?? [.. filePaths];
        if (pathList.Count == 0)
            yield break;

        var echo = ShellCommands.TranslateCommand("echo");
        var readlink = ShellCommands.TranslateCommand("readlink");

        // Inline each path in readlink/echo; a for-loop $link loses quotes through adb/Windows parsing.
        var readlinkScript = string.Join("; ", pathList.Select(path =>
            $"{echo} {EscapeAdbShellString(path + ADB_FIELD_SEP)}$({readlink} -f {EscapeAdbShellString(path)} 2>&1){ADB_FIELD_SEP}"));

        ExecuteDeviceAdbShellCommand(deviceId,
                                     readlinkScript,
                                     out string stdout,
                                     out string stderr,
                                     cancellationToken);

        var linkDict = new Dictionary<string, string>();
        foreach (var line in stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(ADB_FIELD_SEP, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                ExecuteDeviceAdbShellCommand(deviceId,
                                     $"{readlink} {EscapeAdbShellString(parts[0])}",
                                     out string stdout1,
                                     out string stderr1,
                                     cancellationToken);

                if (stdout1.Split([ADB_FIELD_SEP, .. LINE_SEPARATORS], StringSplitOptions.RemoveEmptyEntries) is var parts1 && parts1.Length > 0)
                {
                    linkDict[parts[0]] = parts1[0];
                }

                continue;
            }

            linkDict[parts[0]] = parts[1];
        }

        var uniqueLinks = linkDict.Values.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().Select(l => EscapeAdbShellString(l));

        // Get file mode of all unique link targets
        ExecuteDeviceAdbShellCommand(deviceId, "stat", out string statStdout, out string statStderr, cancellationToken, [.. uniqueLinks, .. STAT_LINKMODE_ARGS]);

        var linkTypes = new Dictionary<string, FileType>();
        foreach (var line in statStdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(ADB_FIELD_SEP);
            if (parts.Length >= 2 && UInt32.TryParse(parts[1].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var modeValue))
                linkTypes[parts[0]] = ParseFileMode((UnixFileMode)modeValue);
        }

        // Iterate over input files using the dictionaries
        foreach (var file in filePaths)
        {
            if (linkDict.TryGetValue(file, out var target))
            {
                if (linkTypes.TryGetValue(target, out var type))
                {
                    yield return (target, type);
                    continue;
                }

                yield return (target, FileType.BrokenLink);
                continue;
            }

            yield return ("", FileType.Unknown);
        }
    }

    public static void ListDirectory(string deviceId, string path, ref ConcurrentQueue<FileStat> output, Dispatcher dispatcher, CancellationToken cancellationToken)
    {
        try
        {
            foreach (var item in ListDirectoryEntries(deviceId, path, cancellationToken))
                output.Enqueue(item);
        }
        catch (Exception e)
        {
            var message = e.Message;
            if (!string.IsNullOrEmpty(message))
                message += "\n\n";

            dispatcher.Invoke(() => DialogService.ShowMessage(message + Strings.Resources.S_LS_ERROR,
                                                              Strings.Resources.S_LS_ERROR_TITLE,
                                                              DialogService.DialogIcon.Critical,
                                                              true,
                                                              copyToClipboard: true,
                                                              error: DialogError.ListDirectoryFailed));
        }
    }

    public static IEnumerable<FileStat> ListDirectoryEntries(string deviceId, string path, CancellationToken cancellationToken)
    {
        foreach (string stdoutLine in ExecuteDeviceAdbCommandAsync(deviceId, "ls", cancellationToken, EscapeAdbString(path)))
        {
            var item = CreateFile(path, stdoutLine);

            if (item is not null)
                yield return item.Value;
        }
    }

    public static IEnumerable<FileStat> ListDirectoryRecursive(string deviceId, string path, CancellationToken cancellationToken)
    {
        var queue = new Queue<string>();
        queue.Enqueue(path);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            IEnumerable<FileStat> entries;
            try
            {
                entries = ListDirectoryEntries(deviceId, current, cancellationToken);
            }
            catch (ProcessFailedException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                yield return entry;

                if (entry.Type is FileType.Folder)
                    queue.Enqueue(entry.FullPath);
            }
        }
    }

    public static string TranslateDevicePath(string deviceId, string path)
    {
        if (path.StartsWith('~'))
            path = path.Length == 1 ? "/" : path[1..];

        if (path.StartsWith("//"))
            path = path[1..];

        int exitCode = ExecuteDeviceAdbShellCommand(deviceId, "cd", out string stdout, out string stderr, CancellationToken.None, EscapeAdbShellString(path), "&&", "pwd");
        if (exitCode != 0)
        {
            throw new Exception(stderr);
        }
        return stdout.TrimEnd(LINE_SEPARATORS);
    }

    public static List<DriveSnapshot> GetDrives(string deviceId, DeviceType deviceType, CancellationToken cancellationToken)
    {
        List<DriveSnapshot> drives = [];

        // unified df doesn't seem to shorten execution time

        var root = ReadDrives(deviceId, deviceType, RE_EMULATED_STORAGE_SINGLE(), cancellationToken, "/");
        if (root is null)
            return null;
        else if (root.Any())
            drives.Add(root.First());

        var intStorage = ReadDrives(deviceId, deviceType, RE_EMULATED_STORAGE_SINGLE(), cancellationToken, "/sdcard");
        if (intStorage is null)
            return drives;
        if (intStorage.Any())
            drives.Add(intStorage.First());

        var extStorage = ReadDrives(deviceId, deviceType, RE_EMULATED_ONLY(), cancellationToken, EMULATED_DRIVES_GREP);
        if (extStorage is null)
            return drives;

        Func<DriveSnapshot, bool> predicate = drives.Any(d => d.Type is AbstractDrive.DriveType.Internal)
            ? d => d.Type is not AbstractDrive.DriveType.Internal and not AbstractDrive.DriveType.Root
            : d => d.Type is not AbstractDrive.DriveType.Root;

        drives.AddRange(extStorage.Where(predicate));

        var tempStorage = ReadDrives(deviceId, deviceType, RE_EMULATED_STORAGE_SINGLE(), cancellationToken, TEMP_PATH);
        if (tempStorage?.Any() is true)
            drives.Add(tempStorage.First() with { Type = AbstractDrive.DriveType.Temp });

        if (drives.All(d => d.Type != AbstractDrive.DriveType.Internal))
            drives.Insert(0, new(Path: "/sdcard", Type: AbstractDrive.DriveType.Internal, Size: "", Used: "", Available: "", UsageP: -1, FileSystem: "", IsEmulator: false));

        if (drives.All(d => d.Type != AbstractDrive.DriveType.Root))
            drives.Insert(0, new(Path: "/", Type: AbstractDrive.DriveType.Root, Size: "", Used: "", Available: "", UsageP: -1, FileSystem: "", IsEmulator: false));

        return drives;
    }

    private static IEnumerable<DriveSnapshot> ReadDrives(string deviceId, DeviceType deviceType, Regex re, CancellationToken cancellationToken, params string[] args)
    {
        int exitCode = ExecuteDeviceAdbShellCommand(deviceId, "df", out string stdout, out string stderr, cancellationToken, args);
        if (exitCode != 0)
            return null;

        return re.Matches(stdout).Select(m => DriveSnapshot.Parse(m.Groups, isEmulator: deviceType is DeviceType.Emulator, forcePath: args[0] == "/" ? "/" : ""));
    }

    private const int BATTERY_PROPERTY_CURRENT_NOW = 2;

    private static readonly string[] BATTERY_CURRENT_NOW_PATHS =
    [
        "/sys/class/power_supply/battery/current_now",
        "/sys/class/power_supply/main/current_now",
        "/sys/class/power_supply/bms/current_now",
    ];

    private static readonly int[] BATTERY_PROPERTIES_TRANSACTIONS = [1, 3];

    public static Dictionary<string, string> GetBatteryInfo(string deviceId, CancellationToken cancellationToken)
    {
        Dictionary<string, string> info = null;

        if (ExecuteDeviceAdbShellCommand(deviceId, BATTERY, out string stdout, out _, cancellationToken) == 0)
        {
            info = stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Where(l => l.Contains(':')).ToDictionary(
                line => line.Split(':')[0].Trim(),
                line => line.Split(':')[1].Trim());
        }

        var currentMicroAmps = TryGetBatteryCurrentMicroAmps(deviceId, cancellationToken);
        if (currentMicroAmps is null && info is null)
            return null;

        info ??= [];
        if (currentMicroAmps is not null)
            info["Current now"] = currentMicroAmps.Value.ToString();

        return info;
    }

    private static long? TryGetBatteryCurrentMicroAmps(string deviceId, CancellationToken cancellationToken)
    {
        foreach (var path in BATTERY_CURRENT_NOW_PATHS)
        {
            if (ExecuteDeviceAdbShellCommand(deviceId, "cat", out string stdout, out _, cancellationToken, path, "2>/dev/null") != 0)
                continue;

            if (long.TryParse(stdout.Trim(LINE_SEPARATORS), out long microAmps))
                return microAmps;
        }

        foreach (var transaction in BATTERY_PROPERTIES_TRANSACTIONS)
        {
            if (ExecuteDeviceAdbShellCommand(deviceId,
                                             "service",
                                             out string stdout,
                                             out _,
                                             cancellationToken,
                                             "call",
                                             "batteryproperties",
                                             transaction.ToString(),
                                             "i32",
                                             BATTERY_PROPERTY_CURRENT_NOW.ToString()) != 0)
                continue;

            var value = ParseBatteryPropertyValue(stdout);
            if (value is not null)
                return value;
        }

        return null;
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

    #endregion
}

/// <summary>
/// Lightweight, fully-parsed snapshot of a single ADB device line.
/// Used for cheap change-detection during polling before any ViewModel is allocated.
/// </summary>
public readonly record struct DeviceSnapshot(
    string ID,
    string Name,
    DeviceStatus Status,
    DeviceType Type,
    RootStatus Root,
    string IpAddress,
    DeviceData DeviceData)
{
    public static DeviceSnapshot Parse(Match match)
    {
        var name = DeviceHelper.ParseDeviceName(match.Groups["model"].Value, match.Groups["device"].Value);
        var id = match.Groups["id"].Value;
        var status = match.Groups["status"].Value;
        var type = DeviceHelper.GetType(id, status);
        var deviceStatus = DeviceHelper.GetStatus(status);
        var ip = type is DeviceType.Remote ? id.Split(':')[0] : "";
        var rootStatus = type is DeviceType.Recovery ? RootStatus.Enabled : RootStatus.Unchecked;

        if (type is DeviceType.WSA && name.Contains("subsystem", StringComparison.InvariantCultureIgnoreCase))
            name = Strings.Resources.S_TYPE_WSA;

        return new(id, name, deviceStatus, type, rootStatus, ip, new DeviceData(match.Value));
    }

    public static implicit operator bool(DeviceSnapshot s) => !string.IsNullOrEmpty(s.ID);
}

public readonly record struct DriveSnapshot(
    string Path,
    AbstractDrive.DriveType Type,
    string Size,
    string Used,
    string Available,
    sbyte UsageP,
    string FileSystem,
    bool IsEmulator,
    string MountPoint = "")
{
    public string ID => Path.Count(c => c == '/') > 1 ? Path[(Path.LastIndexOf('/') + 1)..] : Path;

    public static DriveSnapshot Parse(GroupCollection groups, bool isEmulator, string forcePath)
    {
        var mountPoint = groups["path"].Value.Trim();
        var path = string.IsNullOrEmpty(forcePath) ? mountPoint : forcePath;
        var type = AbstractDrive.DriveType.Unknown;

        // Replicate Drive base ctor: DRIVE_TYPES exact match
        if (DRIVE_TYPES.TryGetValue(path, out var baseType))
        {
            type = baseType;
            if (type is AbstractDrive.DriveType.Internal)
                path = "/sdcard";
        }

        // Replicate LogicalDrive ctor type overrides
        if (path == "/")
            type = AbstractDrive.DriveType.Root;
        else if (DRIVE_TYPES.Where(kv => kv.Value is AbstractDrive.DriveType.Internal).Any(kv => kv.Key.Contains(path)))
        {
            type = AbstractDrive.DriveType.Internal;
            path = "/sdcard";
        }
        else if (isEmulator && type is AbstractDrive.DriveType.Unknown)
            type = AbstractDrive.DriveType.Emulated;

        return new(
            Path: path,
            Type: type,
            Size: (long.Parse(groups["size_kB"].Value) * 1024).BytesToDriveSize(true),
            Used: (long.Parse(groups["used_kB"].Value) * 1024).BytesToDriveSize(true),
            Available: (long.Parse(groups["available_kB"].Value) * 1024).BytesToDriveSize(true),
            UsageP: sbyte.Parse(groups["usage_P"].Value),
            FileSystem: groups["FileSystem"].Value,
            IsEmulator: isEmulator,
            MountPoint: mountPoint);
    }
}

public record struct FileExtraInfo(string User,
                                   string Group,
                                   DateTimeOffset AccessTime,
                                   DateTimeOffset CreationTime,
                                   DateTimeOffset ModifiedTime);
