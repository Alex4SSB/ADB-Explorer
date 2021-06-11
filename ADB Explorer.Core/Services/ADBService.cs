using ADB_Explorer.Core.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ADB_Explorer.Core.Services
{
    public class ADBService
    {
        private const string ADB_PATH = "adb";
        private const string SHELL_CMD = "shell";

        public static int ExecuteShellCommand(string cmd, out string stdout, out string stderr, params string[] args)
        {
            Process cmdProcess = new Process();
            cmdProcess.StartInfo.UseShellExecute = false;
            cmdProcess.StartInfo.RedirectStandardOutput = true;
            cmdProcess.StartInfo.RedirectStandardError = true;
            cmdProcess.StartInfo.CreateNoWindow = true;
            cmdProcess.StartInfo.FileName = ADB_PATH;
            cmdProcess.StartInfo.Arguments = SHELL_CMD + " " + cmd + " " + string.Join(" ", args);
            cmdProcess.Start();

            var stdoutTask = cmdProcess.StandardOutput.ReadToEndAsync();
            var stderrTask = cmdProcess.StandardError.ReadToEndAsync();
            Task.WaitAll(stdoutTask, stderrTask);
            cmdProcess.WaitForExit();

            stdout = stdoutTask.Result;
            stderr = stderrTask.Result;
            return cmdProcess.ExitCode;
        }

        public static List<FileStat> ReadDirectory(string path)
        {
            List<FileStat> result = new List<FileStat>();

            // Remove trailing '/' from given path to avoid possible issues
            if (path.EndsWith('/'))
            {
                path = path.Remove(path.Length - 1);
            }

            // Add parent directory when needed
            if (path.LastIndexOf('/') > 0)
            {
                result.Add(new FileStat
                {
                    Name = "..",
                    Path = path.Remove(path.LastIndexOf('/')),
                    Type = FileStat.FileType.Parent
                });
            }

            // Execute find and stat to get file details in a safe to parse format
            string stdout, stderr;
            int exitCode = ExecuteShellCommand("find", out stdout, out stderr, $"\"{path}/\"", "-maxdepth", "1", "-exec", "\"stat -L -c %F/%s/%Y/%n {} \\;\"");
            if ((exitCode != 0) || (stderr.Length > 0))
            {
                throw new Exception($"{stderr} (Error Code: {exitCode})");
            }

            // Split result by lines
            var fileEntries = stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Skip(1).ToList();
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
    }
}
