using ADB_Explorer.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ADB_Explorer.Services
{
    public partial class ADBService
    {
        public class Device
        {
            private string deviceSerial;

            public string DeviceSerial
            {
                get { return deviceSerial; }
            }

            public Device(string deviceSerial)
            {
                this.deviceSerial = deviceSerial;
            }

            private const string ADB_PROGRESS_HELPER_PATH = "AdbProgressRedirection.exe";

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

            public void ListDirectory(string path, ref ConcurrentQueue<FileStat> output, CancellationToken cancellationToken)
            {
                // Get real path
                path = TranslateDevicePath(path);

                // Execute adb ls to get file list
                var stdout = ExecuteDeviceAdbCommandAsync(deviceSerial, "ls", cancellationToken, EscapeAdbString(path));
                foreach (string stdoutLine in stdout)
                {
                    var match = AdbRegEx.LS_FILE_ENTRY_RE.Match(stdoutLine);
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

            public AdbSyncStatsInfo Pull(
                string targetPath,
                string sourcePath,
                ref ConcurrentQueue<AdbSyncProgressInfo> progressUpdates,
                CancellationToken cancellationToken)
            {
                // Execute adb pull
                var stdout = ExecuteDeviceCommandAsync(
                    deviceSerial,
                    ADB_PROGRESS_HELPER_PATH,
                    ADB_PATH,
                    cancellationToken,
                    Encoding.Unicode,
                    "pull",
                    "-a",
                    EscapeAdbString(sourcePath),
                    EscapeAdbString(targetPath));

                // Each line should be a progress update (but sometimes the output can be weird)
                string lastStdoutLine = null;
                foreach (string stdoutLine in stdout)
                {
                    lastStdoutLine = stdoutLine;
                    var progressMatch = AdbRegEx.FILE_SYNC_PROGRESS_RE.Match(stdoutLine);
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

                var match = AdbRegEx.PULL_STATS_RE.Match(lastStdoutLine);
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

            public bool IsDirectory(string path)
            {
                string stdout, stderr;
                int exitCode = ExecuteDeviceAdbShellCommand(deviceSerial, "cd", out stdout, out stderr, EscapeAdbShellString(path));
                return ((exitCode == 0) || ((exitCode != 0) && stderr.Contains("permission denied", StringComparison.OrdinalIgnoreCase)));
            }

            public string TranslateDevicePath(string path)
            {
                string stdout, stderr;
                int exitCode = ExecuteDeviceAdbShellCommand(deviceSerial, "cd", out stdout, out stderr, EscapeAdbShellString(path), "&&", "pwd");
                if (exitCode != 0)
                {
                    throw new Exception(stderr);
                }
                return stdout.TrimEnd(LINE_SEPARATORS);
            }

            public string TranslateDeviceParentPath(string path) => TranslateDevicePath(ConcatPaths(path, PARENT_DIR));
        }
    }
}
