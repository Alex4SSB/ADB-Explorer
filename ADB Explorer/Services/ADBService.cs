using ADB_Explorer.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static ADB_Explorer.Models.AdbRegEx;

namespace ADB_Explorer.Services
{
    public class ADBService
    {
        private const string ADB_PATH = "adb";
        private const string ADB_PROGRESS_HELPER_PATH = "AdbProgressRedirection.exe";
        private const string PRODUCT_MODEL = "ro.product.model";
        private const string HOST_NAME = "net.hostname";
        private const string VENDOR = "ro.vendor.config.CID";
        private const string GET_PROP = "getprop";
        private const string GET_DEVICES = "devices";

        private const string CURRENT_DIR = ".";
        private const string PARENT_DIR = "..";
        private static readonly string[] SPECIAL_DIRS = { CURRENT_DIR, PARENT_DIR };
        private static readonly char[] LINE_SEPARATORS = { '\n', '\r' };

        public class AdbSyncProgressInfo
        {
            public string CurrentFile { get; set; }
            public int? TotalPrecentage { get; set; }
            public int? CurrentFilePrecentage { get; set; }
            public UInt64? CurrentFileBytesTransferred { get; set; }

            public override string ToString()
            {
                return TotalPrecentage.ToString() + ", " + CurrentFile + ", " + CurrentFilePrecentage?.ToString() + ", " + CurrentFileBytesTransferred?.ToString();
            }
        }

        public class AdbSyncStatsInfo
        {
            public string TargetPath { get; set; }
            public UInt64 FilesPulled { get; set; }
            public UInt64 FilesSkipped { get; set; }
            public decimal? AverageRate { get; set; }
            public UInt64? TotalBytes { get; set; }
            public decimal? TotalTime { get; set; }
        }

        private enum UnixFileMode : UInt32
        {
            S_IFMT = 0b1111 << 12, // bit mask for the file type bit fields
            S_IFSOCK = 0b1100 << 12, // socket
            S_IFLNK = 0b1010 << 12, // symbolic link
            S_IFREG = 0b1000 << 12, // regular file
            S_IFBLK = 0b0110 << 12, // block device
            S_IFDIR = 0b0100 << 12, // directory
            S_IFCHR = 0b0010 << 12, // character device
            S_IFIFO = 0b0001 << 12  // FIFO
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
            cmdProcess.StartInfo.Arguments = string.Join(' ', new[] { cmd }.Concat(args));
            cmdProcess.StartInfo.StandardOutputEncoding = encoding;
            cmdProcess.StartInfo.StandardErrorEncoding = encoding;
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

        public static int ExecuteAdbCommand(string deviceId, string cmd, out string stdout, out string stderr, params string[] args)
        {
            return ExecuteCommand(ADB_PATH, $"{(string.IsNullOrEmpty(deviceId) ? "" : $"-s {deviceId} ")}{cmd}", out stdout, out stderr, Encoding.UTF8, args);
        }

        //public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, params string[] args) =>
        //    ExecuteCommand(ADB_PATH, cmd, out stdout, out stderr, System.Text.Encoding.UTF8, args);

        public static IEnumerable<string> ExecuteCommandAsync(
            string file, string cmd, CancellationToken cancellationToken, Encoding encoding, params string[] args)
        {
            using var cmdProcess = StartCommandProcess(file, cmd, encoding, args);

            var stdoutLine = cmdProcess.StandardOutput.ReadLine();
            while (stdoutLine != null)
            {
                yield return stdoutLine;
                stdoutLine = cmdProcess.StandardOutput.ReadLine();

                if (cancellationToken.IsCancellationRequested)
                {
                    cmdProcess.Kill();
                    yield break;
                }
            }

            string stderr = cmdProcess.StandardError.ReadToEnd();
            cmdProcess.WaitForExit();

            if (cmdProcess.ExitCode != 0)
            {
                throw new ProcessFailedException(cmdProcess.ExitCode, stderr);
            }
        }

        public static IEnumerable<string> ExecuteAdbCommandAsync(string deviceId, string cmd, CancellationToken cancellationToken, params string[] args) =>
            ExecuteCommandAsync(ADB_PATH, $"{(string.IsNullOrEmpty(deviceId) ? "" : $"-s {deviceId} ")}{cmd}", cancellationToken, Encoding.UTF8, args);

        public static int ExecuteAdbShellCommand(string deviceId, string cmd, out string stdout, out string stderr, params string[] args)
        {
            return ExecuteAdbCommand(deviceId, "shell", out stdout, out stderr, new[] { cmd }.Concat(args).ToArray());
        }

        public static string EscapeAdbShellString(string str)
        {
            var result = string.Concat(str.Select(c =>
                c switch
                {
                    var ch when new[] { '(', ')', '<', '>', '|', ';', '&', '*', '\\', '~', '"', '\'', ' ', '$', '`'}.Contains(ch) => "\\" + ch,
                    _ => new string(c, 1)
                }));

            return $"\"{result}\"";
        }

        public static string EscapeAdbString(string str)
        {
            var result = string.Concat(str.Select(c =>
                c switch
                {
                    var ch when new[] { '$', '`', '"', '\\' }.Contains(ch) => "\\" + ch,
                    _ => new string(c, 1)
                }));

            return $"\"{result}\"";
        }

        private static string ConcatPaths(string path1, string path2)
        {
            return path1.TrimEnd('/') + '/' + path2.TrimStart('/');
        }

        public static void ListDirectory(string deviceId, string path, ref ConcurrentQueue<FileStat> output, CancellationToken cancellationToken)
        {
            // Get real path
            path = TranslateDevicePath(deviceId, path);

            // Execute adb ls to get file list
            var stdout = ExecuteAdbCommandAsync(deviceId, "ls", cancellationToken, EscapeAdbString(path));
            foreach (string stdoutLine in stdout)
            {
                var match = LS_FILE_ENTRY_RE.Match(stdoutLine);
                if (!match.Success)
                {
                    throw new Exception($"Invalid output for adb ls command: {stdoutLine}");
                }

                var name = match.Groups["Name"].Value;
                var size = UInt64.Parse(match.Groups["Size"].Value, System.Globalization.NumberStyles.HexNumber);
                var time = long.Parse(match.Groups["Time"].Value, System.Globalization.NumberStyles.HexNumber);
                var mode = UInt32.Parse(match.Groups["Mode"].Value, System.Globalization.NumberStyles.HexNumber);

                if (SPECIAL_DIRS.Contains(name))
                {
                    continue;
                }

                output.Enqueue(new FileStat
                (
                    fileName: name,
                    path: ConcatPaths(path, name),
                    type: (UnixFileMode)(mode & (UInt32)UnixFileMode.S_IFMT) switch
                    {
                        UnixFileMode.S_IFSOCK => FileStat.FileType.Socket,
                        UnixFileMode.S_IFLNK => FileStat.FileType.Unknown,
                        UnixFileMode.S_IFREG => FileStat.FileType.File,
                        UnixFileMode.S_IFBLK => FileStat.FileType.BlockDevice,
                        UnixFileMode.S_IFDIR => FileStat.FileType.Folder,
                        UnixFileMode.S_IFCHR => FileStat.FileType.CharDevice,
                        UnixFileMode.S_IFIFO => FileStat.FileType.FIFO,
                        (UnixFileMode)0 => FileStat.FileType.Unknown,
                        _ => throw new Exception($"Unexpected file type for \"{name}\" with mode: {mode}")
                    },
                    size: (mode != 0) ? size : new UInt64?(),
                    modifiedTime: (time > 0) ? DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime() : new DateTime?(),
                    isLink: (mode & (UInt32)UnixFileMode.S_IFMT) == (UInt32)UnixFileMode.S_IFLNK
                ));
            }
        }

        public static AdbSyncStatsInfo Pull(
            string deviceId,
            string targetPath,
            string sourcePath,
            ref ConcurrentQueue<AdbSyncProgressInfo> progressUpdates,
            CancellationToken cancellationToken)
        {
            // Execute adb pull
            var stdout = ExecuteCommandAsync(
                ADB_PROGRESS_HELPER_PATH,
                ADB_PATH,
                cancellationToken,
                Encoding.Unicode,
                string.IsNullOrEmpty(deviceId) ? "pull" : $"-s {deviceId} pull",
                "-a",
                EscapeAdbString(sourcePath),
                EscapeAdbString(targetPath));

            // Each line should be a progress update (but sometimes the output can be weird)
            string lastStdoutLine = null;
            foreach (string stdoutLine in stdout)
            {
                lastStdoutLine = stdoutLine;
                var progressMatch = PULL_PROGRESS_RE.Match(stdoutLine);
                if (!progressMatch.Success)
                {
                    continue;
                }

                var currFile = progressMatch.Groups["CurrentFile"].Value;

                string totalPrecentageRaw = progressMatch.Groups["TotalPrecentage"].Value;
                int? totalPrecentage = totalPrecentageRaw.EndsWith("%") ? int.Parse(totalPrecentageRaw.TrimEnd('%')) : null;

                int? currPrecentage = null;
                if (progressMatch.Groups["CurrentPrecentage"].Success)
                {
                    string currPrecentageRaw = progressMatch.Groups["CurrentPrecentage"].Value;
                    currPrecentage = currPrecentageRaw.EndsWith("%") ? int.Parse(currPrecentageRaw.TrimEnd('%')) : null;
                }

                UInt64? currBytes =
                    progressMatch.Groups["CurrentBytes"].Success ?
                    UInt64.Parse(progressMatch.Groups["CurrentBytes"].Value) : null;

                progressUpdates.Enqueue(new AdbSyncProgressInfo
                {
                    TotalPrecentage = totalPrecentage,
                    CurrentFile = currFile,
                    CurrentFilePrecentage = currPrecentage,
                    CurrentFileBytesTransferred = currBytes
                });
            }

            if (lastStdoutLine == null)
            {
                return null;
            }

            var match = PULL_STATS_RE.Match(lastStdoutLine);
            if (!match.Success)
            {
                return null;
            }

            var path = match.Groups["TargetPath"].Value;
            UInt64 totalPulled = UInt64.Parse(match.Groups["TotalPulled"].Value);
            UInt64 totalSkipped = UInt64.Parse(match.Groups["TotalSkipped"].Value);
            decimal? avrageRate = match.Groups["AverageRate"].Success ? decimal.Parse(match.Groups["AverageRate"].Value) : null;
            UInt64? totalBytes = match.Groups["TotalBytes"].Success ? UInt64.Parse(match.Groups["TotalBytes"].Value) : null;
            decimal? totalTime = match.Groups["TotalTime"].Success ? decimal.Parse(match.Groups["TotalTime"].Value) : null;

            return new AdbSyncStatsInfo
            {
                TargetPath = path,
                FilesPulled = totalPulled,
                FilesSkipped = totalSkipped,
                AverageRate = avrageRate,
                TotalBytes = totalBytes,
                TotalTime = totalTime
            };
        }

        public static bool IsDirectory(string deviceId, string path)
        {
            string stdout, stderr;
            int exitCode = ExecuteAdbShellCommand(deviceId, "cd", out stdout, out stderr, EscapeAdbShellString(path));
            return ((exitCode == 0) || ((exitCode != 0) && stderr.Contains("permission denied", StringComparison.OrdinalIgnoreCase)));
        }

        public static string TranslateDeviceParentPath(string deviceId, string path)
        {
            return TranslateDevicePath(deviceId, ConcatPaths(path, PARENT_DIR));
        }

        public static string TranslateDevicePath(string deviceId, string path)
        {
            string stdout, stderr;
            int exitCode = ExecuteAdbShellCommand(deviceId, "cd", out stdout, out stderr, EscapeAdbShellString(path), "&&", "pwd");
            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }
            return stdout.TrimEnd(LINE_SEPARATORS);
        }

        public static List<DeviceClass> GetDevices()
        {
            ExecuteAdbCommand("", GET_DEVICES, out string stdout, out string stderr, "-l");

            return DEVICE_NAME_RE.Matches(stdout).Select(
                m => new DeviceClass(
                    name: m.Groups["name"].Value.Replace('_', ' '),
                    id: m.Groups["id"].Value,
                    type: m.Groups["id"].Value.Contains('.') ? (m.Groups["status"].Value == "device" ? DeviceClass.DeviceType.Remote : DeviceClass.DeviceType.Offline) : DeviceClass.DeviceType.Local)
                ).ToList();
        }

        //private static string GetProps()
        //{
        //    ExecuteAdbShellCommand(GET_PROP, out string stdout, out string stderr);
        //    return stdout;
        //}

        private static string GetPropsValue(string props, string key)
        {
            var value = props.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Where(s => s.Contains(key));
            return value.Any() ? value.First().Split('[', ']')[3] : "";
        }

        //public static void ConnectNetworkDevice(string host, string port) => NetworkDeviceOperation("connect", host, UInt16.Parse(port));
        //public static void ConnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("connect", host, port);
        //public static void DisconnectNetworkDevice(string host, string port) => NetworkDeviceOperation("disconnect", host, UInt16.Parse(port));
        //public static void DisconnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("disconnect", host, port);

        public static void ConnectNetworkDevice(string host, string port) => NetworkDeviceOperation("connect", $"{host}:{port}");
        public static void ConnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("connect", fullAddress);
        public static void DisonnectNetworkDevice(string host, string port) => NetworkDeviceOperation("disconnect", $"{host}:{port}");
        public static void DisconnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("disconnect", fullAddress);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cmd">connect / disconnect</param>
        /// <param name="host">IP address of remote device</param>
        /// <param name="port">ADB port of remote device</param>
        /// <exception cref="ConnectionRefusedException"></exception>
        /// <exception cref="ConnectionTimeoutException"></exception>
        //private static void NetworkDeviceOperation(string cmd, string host, UInt16 port)
        //{
        //    ExecuteAdbCommand("", cmd, out string stdout, out _, $"{host}:{port}");
        //    if (stdout.Contains("cannot connect") || stdout.Contains("error"))
        //    {
        //        throw new Exception(stdout);
        //    }
        //}

        /// <summary>
        /// 
        /// </summary>
        /// <param name="cmd">connect / disconnect</param>
        /// <param name="host">IP address of remote device</param>
        /// <param name="port">ADB port of remote device</param>
        /// <exception cref="ConnectionRefusedException"></exception>
        /// <exception cref="ConnectionTimeoutException"></exception>
        private static void NetworkDeviceOperation(string cmd, string fullAddress)
        {
            ExecuteAdbCommand("", cmd, out string stdout, out _, fullAddress);
            if (stdout.Contains("cannot connect") || stdout.Contains("error"))
            {
                throw new Exception(stdout);
            }
        }
    }
}
