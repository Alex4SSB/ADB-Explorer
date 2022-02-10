using ADB_Explorer.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.AdbRegEx;

namespace ADB_Explorer.Services
{
    public partial class ADBService
    {
        private static string adbPath = "";
        private static string ADB_PATH
        {
            get
            {
                if (adbPath == "")
                {
                    adbPath = Storage.RetrieveValue(UserPrefs.manualAdbPath) is string path ? $"\"{path}\"" : "adb";
                }
                return adbPath;
            }
        }

        private const string GET_DEVICES = "devices";
        private const string ENABLE_MDNS = "ADB_MDNS_OPENSCREEN";

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
            InitProcess(cmdProcess);
            cmdProcess.StartInfo.FileName = file;
            cmdProcess.StartInfo.Arguments = $"{cmd} {string.Join(' ', args)}";
            cmdProcess.StartInfo.StandardOutputEncoding = encoding;
            cmdProcess.StartInfo.StandardErrorEncoding = encoding;
            
            if (IsMdnsEnabled)
                cmdProcess.StartInfo.EnvironmentVariables[ENABLE_MDNS] = "1";

            cmdProcess.Start();
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
            ExecuteCommand(ADB_PATH, cmd, out stdout, out stderr, System.Text.Encoding.UTF8, args);

        public static int ExecuteDeviceAdbCommand(string deviceSerial, string cmd, out string stdout, out string stderr, params string[] args)
        {
            return ExecuteAdbCommand("-s", out stdout, out stderr, new[] { deviceSerial, cmd }.Concat(args).ToArray());
        }

        public static IEnumerable<string> ExecuteCommandAsync(
            string file, string cmd, CancellationToken cancellationToken, Encoding encoding, params string[] args)
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
                catch (OperationCanceledException e)
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
            catch (OperationCanceledException e)
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
            ExecuteCommandAsync(ADB_PATH, cmd, cancellationToken, Encoding.UTF8, args);

        public static IEnumerable<string> ExecuteDeviceAdbCommandAsync(string deviceSerial, string cmd, CancellationToken cancellationToken, params string[] args)
        {
            return ExecuteAdbCommandAsync("-s", cancellationToken, new[] { deviceSerial, cmd }.Concat(args).ToArray());
        }

        public static int ExecuteDeviceAdbShellCommand(string deviceId, string cmd, out string stdout, out string stderr, params string[] args)
        {
            return ExecuteDeviceAdbCommand(deviceId, "shell", out stdout, out stderr, new[] { cmd }.Concat(args).ToArray());
        }

        public static string EscapeAdbShellString(string str)
        {
            var result = string.Concat(str.Select(c =>
                c switch
                {
                    var ch when ESCAPE_ADB_SHELL_CHARS.Contains(ch) => "\\" + ch,
                    _ => new string(c, 1)
                }));

            return $"\"{result}\"";
        }

        public static string EscapeAdbString(string str)
        {
            var result = string.Concat(str.Select(c =>
                c switch
                {
                    var ch when ESCAPE_ADB_CHARS.Contains(ch) => "\\" + ch,
                    _ => new string(c, 1)
                }));

            return $"\"{result}\"";
        }

        private static string ConcatPaths(string path1, string path2)
        {
            return path1.TrimEnd('/') + '/' + path2.TrimStart('/');
        }

        public static IEnumerable<LogicalDevice> GetDevices()
        {
            ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, "-l");

            return DEVICE_NAME_RE.Matches(stdout).Select(
                m => LogicalDevice.New(
                    name: LogicalDevice.DeviceName(m.Groups["model"].Value, m.Groups["device"].Value),
                    id: m.Groups["id"].Value,
                    status: m.Groups["status"].Value)
                ).Where(d => d);
        }

        public static void ConnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("connect", $"{host}:{port}");
        public static void ConnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("connect", fullAddress);
        public static void DisonnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("disconnect", $"{host}:{port}");
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
            return MMC_BLOCK_DEVICE_NODE.Match(stdout).Groups;
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

        public static bool Root(Device device)
        {
            ExecuteDeviceAdbCommand(device.ID, "root", out string stdout, out string stderr);
            return !stdout.Contains("cannot run as root");
        }

        public static bool Unroot(Device device)
        {
            ExecuteDeviceAdbCommand(device.ID, "unroot", out string stdout, out string stderr);
            return stdout.Contains("restarting adbd as non root");
        }
    }
}
