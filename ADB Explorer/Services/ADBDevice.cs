using ADB_Explorer.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace ADB_Explorer.Services
{
    public partial class ADBService
    {
        //private const string GET_PROP = "getprop";
        //private const string PRODUCT_MODEL = "ro.product.model";
        //private const string HOST_NAME = "net.hostname";
        //private const string VENDOR = "ro.vendor.config.CID";

        private static readonly string[] MMC_BLOCK_DEVICES = { "/dev/block/mmcblk0p1", "/dev/block/mmcblk1p1" }; // first partition

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
                public int? TotalPercentage { get; set; }
                public int? CurrentFilePercentage { get; set; }
                public UInt64? CurrentFileBytesTransferred { get; set; }

                public override string ToString()
                {
                    return TotalPercentage.ToString() + ", " + CurrentFile + ", " + CurrentFilePercentage?.ToString() + ", " + CurrentFileBytesTransferred?.ToString();
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

            public AdbSyncStatsInfo PullFile(
                string targetPath,
                string sourcePath,
                ref ConcurrentQueue<AdbSyncProgressInfo> progressUpdates,
                CancellationToken cancellationToken) =>
                DoFileSync("pull", "-a", targetPath, sourcePath, ref progressUpdates, cancellationToken);

            public AdbSyncStatsInfo PushFile(
                string targetPath,
                string sourcePath,
                ref ConcurrentQueue<AdbSyncProgressInfo> progressUpdates,
                CancellationToken cancellationToken) =>
                DoFileSync("push", "", targetPath, sourcePath, ref progressUpdates, cancellationToken);

            private AdbSyncStatsInfo DoFileSync(
                string opertation,
                string operationArgs,
                string targetPath,
                string sourcePath,
                ref ConcurrentQueue<AdbSyncProgressInfo> progressUpdates,
                CancellationToken cancellationToken)
            {
                // Execute adb file sync operation
                var stdout = ExecuteCommandAsync(
                    ADB_PROGRESS_HELPER_PATH,
                    ADB_PATH,
                    cancellationToken,
                    Encoding.Unicode,
                    "-s",
                    deviceSerial,
                    opertation,
                    operationArgs,
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

                    string totalPercentageRaw = progressMatch.Groups["TotalPercentage"].Value;
                    int? totalPercentage = totalPercentageRaw.EndsWith("%") ? int.Parse(totalPercentageRaw.TrimEnd('%')) : null;

                    int? currPercentage = null;
                    if (progressMatch.Groups["CurrentPercentage"].Success)
                    {
                        string currPercentageRaw = progressMatch.Groups["CurrentPercentage"].Value;
                        currPercentage = currPercentageRaw.EndsWith("%") ? int.Parse(currPercentageRaw.TrimEnd('%')) : null;
                    }

                    UInt64? currBytes =
                        progressMatch.Groups["CurrentBytes"].Success ?
                        UInt64.Parse(progressMatch.Groups["CurrentBytes"].Value) : null;

                    progressUpdates.Enqueue(new AdbSyncProgressInfo
                    {
                        TotalPercentage = totalPercentage,
                        CurrentFile = currFile,
                        CurrentFilePercentage = currPercentage,
                        CurrentFileBytesTransferred = currBytes
                    });
                }

                if (lastStdoutLine == null)
                {
                    return null;
                }

                var match = AdbRegEx.FILE_SYNC_STATS_RE.Match(lastStdoutLine);
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

            public List<Drive> GetStorageInfo()
            {
                // Get all device partitions
                int exitCode = ExecuteDeviceAdbShellCommand(deviceSerial, "df", out string stdout, out string stderr);
                if (exitCode != 0)
                    return new();

                var match = AdbRegEx.EMULATED_STORAGE_SIZE.Matches(stdout);
                //if (match.Count > 2) // 2 matches means only root and internal storage, so no need to look for the MMC
                //{
                //    var mmcId = GetMmcId(deviceSerial);
                //    return match.Select(m => new Drive(m.Groups, isMMC: m.Groups["path"].Value.Contains(mmcId))).ToList();
                //}
                //else
                return match.Select(m => new Drive(m.Groups, isEmulator: deviceSerial.Contains("emulator"))).ToList();
            }

            //private Dictionary<string, string> props { get; set; }
            //public Dictionary<string, string> Props
            //{
            //    get
            //    {
            //        if (props is null)
            //        {
            //            int exitCode = ExecuteDeviceAdbShellCommand(deviceSerial, GET_PROP, out string stdout, out string stderr);
            //            if (exitCode == 0)
            //            {
            //                props = stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).ToDictionary(
            //                line => line.Split(':')[0].Trim('[', ']', ' '),
            //                line => line.Split(':')[1].Trim('[', ']', ' '));
            //            }
            //        }
            //        return props;
            //    }
            //}
        }
    }
}
