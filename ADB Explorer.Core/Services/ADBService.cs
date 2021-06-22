using ADB_Explorer.Core.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Explorer.Core.Services
{
    public class ADBService
    {
        private const string ADB_PATH = "adb";
        private const string PRODUCT_MODEL = "ro.product.model";
        private const string HOST_NAME = "net.hostname";
        private const string VENDOR = "ro.vendor.config.CID";
        private const string GET_PROP = "getprop";
        private const string GET_DEVICES = "devices";

        private static readonly char[] LINE_SEPARATORS = { '\n', '\r' };
        private static readonly string[] SPECIAL_DIRS = { ".", ".." };

        private static readonly Regex LS_FILE_ENTRY_RE = new Regex(
            @"^(?<Mode>[0-9a-f]+) (?<Size>[0-9a-f]+) (?<Time>[0-9a-f]+) (?<Name>[^/]+?)\r?$",
            RegexOptions.IgnoreCase);

        private static readonly Regex DEVICE_NAME_RE = new Regex(@"(?<=device:)\w+");

        private enum UnixFileMode : UInt32
        {
            S_IFMT =   0b1111 << 12, // bit mask for the file type bit fields
            S_IFSOCK = 0b1100 << 12, // socket
            S_IFLNK =  0b1010 << 12, // symbolic link
            S_IFREG =  0b1000 << 12, // regular file
            S_IFBLK =  0b0110 << 12, // block device
            S_IFDIR =  0b0100 << 12, // directory
            S_IFCHR =  0b0010 << 12, // character device
            S_IFIFO =  0b0001 << 12  // FIFO
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

        public static Process StartCommandProcess(string cmd, params string[] args)
        {
            var cmdProcess = new Process();
            InitProcess(cmdProcess);
            cmdProcess.StartInfo.FileName = ADB_PATH;
            cmdProcess.StartInfo.Arguments = string.Join(' ', new[] { cmd }.Concat(args));
            cmdProcess.StartInfo.StandardOutputEncoding = System.Text.Encoding.UTF8;
            cmdProcess.StartInfo.StandardErrorEncoding = System.Text.Encoding.UTF8;
            cmdProcess.Start();
            return cmdProcess;
        }

        public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, params string[] args)
        {
            using var cmdProcess = StartCommandProcess(cmd, args);

            using var stdoutTask = cmdProcess.StandardOutput.ReadToEndAsync();
            using var stderrTask = cmdProcess.StandardError.ReadToEndAsync();
            Task.WaitAll(stdoutTask, stderrTask);
            cmdProcess.WaitForExit();

            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;
            return cmdProcess.ExitCode;
        }

        public static IEnumerable<string> ExecuteAdbCommandAsync(CancellationToken cancellationToken, string cmd, params string[] args)
        {
            using var cmdProcess = StartCommandProcess(cmd, args);

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

        public static int ExecuteShellCommand(string cmd, out string stdout, out string stderr, params string[] args)
        {
            return ExecuteAdbCommand("shell", out stdout, out stderr, new[] { cmd }.Concat(args).ToArray());
        }

        public static string EscapeAdbShellString(string str)
        {
            var result = string.Concat(str.Select(c =>
                c switch
                {
                    var ch when new[] { '(', ')', '<', '>', '|', ';', '&', '*', '\\', '~', '"', '\'', ' ' }.Contains(ch) => "\\" + ch,
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

        public static void ListDirectory(string path, ref ConcurrentQueue<FileStat> output, CancellationToken cancellationToken)
        {
            // Get real path
            path = TranslateDevicePath(path);

            // Add parent directory when needed
            if (path != "/")
            {
                output.Enqueue(new FileStat
                (
                    fileName: "..",
                    path: TranslateDevicePath(ConcatPaths(path, "..")),
                    type: FileStat.FileType.Parent,
                    size: 0,
                    modifiedTime: DateTimeOffset.FromUnixTimeSeconds(0).DateTime
                ));
            }

            // Execute adb ls to get file list
            var stdout = ExecuteAdbCommandAsync(cancellationToken, "ls", EscapeAdbString(path));
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
                        UnixFileMode.S_IFDIR => FileStat.FileType.Folder,
                        UnixFileMode.S_IFREG => FileStat.FileType.File,
                        UnixFileMode.S_IFLNK => FileStat.FileType.Folder, // Links are assumed to be folders
                        _ => FileStat.FileType.File // Other types are assumed to be files
                    },
                    size: size,
                    modifiedTime: DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime()
                ));
            }
        }

        public static string TranslateDevicePath(string path)
        {
            string stdout, stderr;
            int exitCode = ExecuteShellCommand("cd", out stdout, out stderr, EscapeAdbShellString(path), "&&", "pwd");
            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }
            return stdout.TrimEnd(LINE_SEPARATORS);
        }

        public static string GetDeviceName(int index = 0)
        {
            ExecuteAdbCommand(GET_DEVICES, out string stdout, out string stderr, "-l");
            var collection = DEVICE_NAME_RE.Matches(stdout);

            return collection.Count > index
                ? collection[index].Value.Replace('_', ' ')
                : "";
        }

        private static string GetProps()
        {
            ExecuteShellCommand(GET_PROP, out string stdout, out string stderr);
            return stdout;
        }

        private static string GetPropsValue(string props, string key)
        {
            var value = props.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Where(s => s.Contains(key));
            return value.Any() ? value.First().Split('[', ']')[3] : "";
        }
    }
}
