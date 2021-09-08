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
    public partial class ADBService
    {
        
        private const string ADB_PATH = "adb";
        private const string GET_DEVICES = "devices";

        //private const string PRODUCT_MODEL = "ro.product.model";
        //private const string HOST_NAME = "net.hostname";
        //private const string VENDOR = "ro.vendor.config.CID";
        //private const string GET_PROP = "getprop";

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

        public static IEnumerable<string> ExecuteDeviceCommandAsync(
            string deviceSerial, string file, string cmd, CancellationToken cancellationToken, Encoding encoding, params string[] args)
        {
            return ExecuteCommandAsync(file, "-s", cancellationToken, encoding, new[] { deviceSerial, cmd }.Concat(args).ToArray());
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

        public static List<DeviceClass> GetDevices()
        {
            ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, "-l");

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

        //private static string GetPropsValue(string props, string key)
        //{
        //    var value = props.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Where(s => s.Contains(key));
        //    return value.Any() ? value.First().Split('[', ']')[3] : "";
        //}

        public static void ConnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("connect", $"{host}:{port}");
        public static void ConnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("connect", fullAddress);
        public static void DisonnectNetworkDevice(string host, UInt16 port) => NetworkDeviceOperation("disconnect", $"{host}:{port}");
        public static void DisconnectNetworkDevice(string fullAddress) => NetworkDeviceOperation("disconnect", fullAddress);

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
            ExecuteAdbCommand(cmd, out string stdout, out _, fullAddress);
            if (stdout.Contains("cannot connect") || stdout.Contains("error"))
            {
                throw new Exception(stdout);
            }
        }
    }
}
