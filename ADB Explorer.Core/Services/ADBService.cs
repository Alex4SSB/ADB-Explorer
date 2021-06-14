using ADB_Explorer.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
        private static readonly char[] LINE_SEPARATORS = { '\n', '\r' };

        private static void InitProcess(Process cmdProcess)
        {
            cmdProcess.StartInfo.UseShellExecute = false;
            cmdProcess.StartInfo.RedirectStandardOutput = true;
            cmdProcess.StartInfo.RedirectStandardError = true;
            cmdProcess.StartInfo.CreateNoWindow = true;
        }

        public static int ExecuteAdbCommand(string cmd, out string stdout, out string stderr, params string[] args)
        {
            using var cmdProcess = new Process();
            InitProcess(cmdProcess);
            cmdProcess.StartInfo.FileName = ADB_PATH;
            cmdProcess.StartInfo.Arguments = string.Join(' ', new[] { cmd }.Concat(args));
            cmdProcess.Start();

            using var stdoutTask = cmdProcess.StandardOutput.ReadToEndAsync();
            using var stderrTask = cmdProcess.StandardError.ReadToEndAsync();
            Task.WaitAll(stdoutTask, stderrTask);
            cmdProcess.WaitForExit();

            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;
            return cmdProcess.ExitCode;
        }

        public static int ExecuteShellCommand(string cmd, out string stdout, out string stderr, params string[] args)
        {
            return ExecuteAdbCommand("shell", out stdout, out stderr, new[] { cmd }.Concat(args).ToArray());
        }

        public static string EscapeShellString(string str)
        {
            return string.Concat(str.Select(c =>
                c switch
                {
                    var ch when new[] { '(', ')', '<', '>', '|' ,';', '&', '*', '\\', '~', '"', '\'', ' ' }.Contains(ch) => "\\" + ch,
                    _ => new string(c, 1)
                }));
        }

        public static List<FileStat> ReadDirectory(string path)
        {
            List<FileStat> result = new List<FileStat>();

            // Remove trailing '/' from given path to avoid possible issues
            path = path.TrimEnd('/');

            // Add parent directory when needed
            if (path.LastIndexOf('/') is var index && index > 0)
            {
                result.Add(new FileStat
                {
                    Name = "..",
                    Path = path.Remove(index),
                    Type = FileStat.FileType.Parent
                });
            }

            // Execute find and stat to get file details in a safe to parse format
            string stdout, stderr;
            int exitCode = ExecuteShellCommand(
                "find",
                out stdout,
                out stderr,
                $"\"{EscapeShellString(path)}/\"",
                "-maxdepth",
                "1",
                "-exec",
                "\"stat -L -c %F/%s/%Y/%n {} \\;\"");

            if (exitCode != 0)
            {
                throw new Exception($"{stderr} (Error Code: {exitCode})");
            }

            // Split result by lines
            var fileEntries = stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Skip(1);
            foreach (var fileEntry in fileEntries)
            {
                // Split each line to its parameters
                var fileDetails = fileEntry.Split('/', 4, StringSplitOptions.RemoveEmptyEntries);

                FileStat fileStat = new FileStat
                {
                    Name = System.IO.Path.GetFileName(fileDetails[3]),
                    Path = fileDetails[3],
                    Type = fileDetails[0] switch
                    {
                        "directory" => FileStat.FileType.Folder,
                        "regular file" => FileStat.FileType.File,
                        "regular empty file" => FileStat.FileType.File,
                        _ => throw new Exception("Cannot handle file type: " + fileDetails[0])
                    },
                    Size = UInt64.Parse(fileDetails[1]),
                    ModifiedTime = DateTimeOffset.FromUnixTimeSeconds(long.Parse(fileDetails[2])).DateTime
                };

                result.Add(fileStat);
            }

            return result;
        }

        public static string GetDeviceName()
        {
            var stdout = GetProps();
            var hostName = GetPropsValue(stdout, HOST_NAME);

            if (string.IsNullOrEmpty(hostName))
            {
                stdout = GetPropsValue(stdout, PRODUCT_MODEL).Replace('_', ' ');
            }
            else
            {
                var vendor = GetPropsValue(stdout, VENDOR);
                stdout = string.IsNullOrEmpty(stdout)
                    ? GetPropsValue(stdout, PRODUCT_MODEL)
                    : $"{vendor} {hostName}";
            }

            return stdout;
        }

        private static string GetProps()
        {
            ExecuteShellCommand(GET_PROP, out string stdout, out string stderr);
            return stdout;
        }

        private static string GetPropsValue(string props, string key)
        {
            return props.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).First(s => s.Contains(key)).Split('[', ']')[3];
        }
    }
}
