using AdvancedSharpAdbClient;
using AdvancedSharpAdbClient.Models;
using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.AdbRegEx;
using static ADB_Explorer.Models.Data;

namespace ADB_Explorer.Services;

public partial class AdbService
{
    private const string GET_DEVICES = "devices";
    private const string ENABLE_MDNS = "ADB_MDNS_OPENSCREEN";

    public static bool IsMdnsEnabled { get; set; }

    private static int _activeCommandCount = 0;
    public static bool IsCommandActive => _activeCommandCount > 0;

    private static readonly ConcurrentDictionary<int, Process> ActiveCommandProcesses = new();

    public static event Action<bool>? CommandActiveChanged;

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
        public string? StandardError { get; set; }
    };

    public static Process StartCommandProcess(string file, string cmd, Encoding encoding, bool redirect = true, Process? cmdProcess = null, string? workingDir = null, params string[] args)
    {
        cmdProcess ??= new();
        var arguments = string.Join(' ', args.Prepend(cmd).Where(arg => !string.IsNullOrEmpty(arg)));

        cmdProcess.StartInfo.UseShellExecute = false;

        cmdProcess.StartInfo.RedirectStandardOutput =
        cmdProcess.StartInfo.RedirectStandardError =
        cmdProcess.StartInfo.CreateNoWindow = redirect;

        cmdProcess.StartInfo.WorkingDirectory = workingDir ?? "";
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
#if DEBUG
        var summary = FormatProcessSummary(file, cmd, args);
        ApkIconService.MarkLoadStep($"ADB start: {summary}");
        var sw = Stopwatch.StartNew();
#endif

        UpdateCommandActive(1);
        using var cmdProcess = StartCommandProcess(file, cmd, encoding, args: args);
        ActiveCommandProcesses[cmdProcess.Id] = cmdProcess;
        using var cancellationRegistration = cancellationToken.Register(() => KillTrackedProcess(cmdProcess));

        var stdoutTask = cmdProcess.StandardOutput.ReadToEndAsync();
        var stderrTask = cmdProcess.StandardError.ReadToEndAsync();

        var processTask = cmdProcess.WaitForExitAsync(cancellationToken);

        try
        {
            Task.WaitAll([stdoutTask, stderrTask, processTask], cancellationToken);

            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;
#if DEBUG
            ApkIconService.MarkLoadStep(
                $"ADB done exit={cmdProcess.ExitCode} out={stdout.Length}B err={stderr.Length}B ({sw.ElapsedMilliseconds}ms): {summary}");
#endif
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

#if DEBUG
            ApkIconService.MarkLoadStep($"ADB cancelled ({sw.ElapsedMilliseconds}ms): {summary}");
#endif
            return -1;
        }
        finally
        {
            ActiveCommandProcesses.TryRemove(cmdProcess.Id, out _);
            UpdateCommandActive(-1);
        }
    }

#if DEBUG
    private static string FormatProcessSummary(string file, string cmd, string[]? args)
    {
        var name = string.IsNullOrEmpty(file) ? "" : file;
        if (args is null || args.Length == 0)
            return string.IsNullOrEmpty(cmd) ? name : $"{name} {cmd}".Trim();

        return $"{name} {cmd} {string.Join(' ', args)}".Trim();
    }
#endif

    /// <summary>The output that explains a failed command: stderr, or stdout for tools that report errors there.</summary>
    public static string ErrorOutput(string stdout, string stderr) => string.IsNullOrWhiteSpace(stderr) ? stdout : stderr;

    /// <summary>Throws the command's <see cref="ErrorOutput"/> as an <see cref="IOException"/> if it failed - or the cancellation, if canceled.</summary>
    public static void ThrowIfFailed(int exitCode, string stdout, string stderr, CancellationToken cancellationToken = default)
    {
        if (exitCode == 0)
            return;

        cancellationToken.ThrowIfCancellationRequested();
        throw new IOException(ErrorOutput(stdout, stderr));
    }

    public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, CancellationToken cancellationToken, params string[] args)
    {
        // No adb resolved yet (startup, setup mode): fail like a missing adb.exe rather than throw.
        if (string.IsNullOrEmpty(AdbHelper.CurrentAdbState.Path))
        {
            stdout = "";
            stderr = "";
            return -1;
        }

        try
        {
            var result = ExecuteCommand(AdbHelper.CurrentAdbState.Path, cmd, out stdout, out stderr, Encoding.UTF8, cancellationToken, args);
            DiskUsagePollingService.LastServerResponse = DateTime.Now;
            return result;
        }
        catch (Win32Exception)
        {
            AdbHelper.EnterAdbSetupMode();
            stdout = "";
            stderr = "";
            return -1;
        }
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
        string file, string cmd, Encoding encoding, CancellationToken cancellationToken, bool redirect = true, Process? process = null, string? workingDir = null, params string[] args)
    {
#if DEBUG
        var summary = FormatProcessSummary(file, cmd, args);
        ApkIconService.MarkLoadStep($"ADB async start: {summary}");
        var sw = Stopwatch.StartNew();
#endif

        UpdateCommandActive(1);
        var processId = -1;
        var exitCode = -1;
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
                    try
                    {
                        outputQueue.Add(e.Data, cancellationToken);
                    }
                    catch (OperationCanceledException)
                    { }
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
        exitCode = cmdProcess.ExitCode;

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
#if DEBUG
            ApkIconService.MarkLoadStep($"ADB async done exit={exitCode} ({sw.ElapsedMilliseconds}ms): {summary}");
#endif
        }
    }

    public static IEnumerable<string> ExecuteAdbCommandAsync(string cmd, CancellationToken cancellationToken, params string[] args)
    {
        using var enumerator = ExecuteCommandAsync(AdbHelper.CurrentAdbState.Path, cmd, Encoding.UTF8, cancellationToken, args: args).GetEnumerator();
        while (true)
        {
            bool moved;
            try
            {
                moved = enumerator.MoveNext();
            }
            catch (OperationCanceledException)
            {
                yield break;
            }
            catch (Win32Exception)
            {
                AdbHelper.EnterAdbSetupMode();
                yield break;
            }

            if (!moved)
                yield break;

            yield return enumerator.Current;
        }
    }

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

    public static bool CheckMDNS()
    {
        var exitCode = ExecuteAdbCommand("mdns", out string stdout, out _, CancellationToken.None, "check");

        return exitCode == 0 && stdout.Contains("mdns daemon version");
    }

    public static void KillAdbServer(bool restart = false)
    {
        ExecuteAdbCommand("kill-server", out _, out _, CancellationToken.None);

        if (restart)
            ExecuteAdbCommand("start-server", out _, out _, CancellationToken.None);
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

    public static readonly char[] LINE_SEPARATORS = ['\n', '\r'];

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

public readonly record struct DrivePollResult(
    List<DriveSnapshot> Drives,
    long? RecycleCount = null,
    ulong? PackagesCount = null,
    ulong? InstallersCount = null);

public readonly record struct DriveSnapshot(
    string Path,
    AbstractDrive.DriveType Type,
    string Size,
    string Used,
    string Available,
    sbyte UsageP,
    string FileSystem,
    bool IsEmulator,
    string MountPoint = "",
    string? Manufacturer = null,
    string? VolumeLabel = null)
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
                                   System.IO.UnixFileMode? Permissions,
                                   DateTimeOffset AccessTime,
                                   DateTimeOffset ModifiedTime,
                                   long? Size,
                                   int? OwnerUid = null,
                                   int? OwnerGid = null);
