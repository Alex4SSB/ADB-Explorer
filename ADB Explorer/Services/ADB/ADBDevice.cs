using ADB_Explorer.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using static ADB_Explorer.Converters.FileTypeClass;

namespace ADB_Explorer.Services
{
    public partial class ADBService
    {
        private const string GET_PROP = "getprop";
        private const string ANDROID_VERSION = "ro.build.version.release";
        private const string BATTERY = "dumpsys battery";
        private const string MMC_PROP = "vold.microsd.uuid";
        private const string OTG_PROP = "vold.otgstorage.uuid";

        /// <summary>
        /// First partition of MMC block device 0 / 1
        /// </summary>
        private static readonly string[] MMC_BLOCK_DEVICES = { "/dev/block/mmcblk0p1", "/dev/block/mmcblk1p1" };

        private static readonly string[] EMULATED_DRIVES_GREP = { "|", "grep", "-E", "'/mnt/media_rw/|/storage/'" };

        public class AdbDevice : Device
        {
            public AdbDevice(UIDevice other)
            {
                ID = other.Device.ID;
            }

            public AdbDevice(LogicalDevice other)
            {
                ID = other.ID;
            }

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
            }

            public class AdbSyncStatsInfo
            {
                public string TargetPath { get; set; }
                public UInt64 FilesTransferred { get; set; }
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
                var stdout = ExecuteDeviceAdbCommandAsync(ID, "ls", cancellationToken, EscapeAdbString(path));
                foreach (string stdoutLine in stdout)
                {
                    var match = AdbRegEx.RE_LS_FILE_ENTRY.Match(stdoutLine);
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
                            UnixFileMode.S_IFSOCK => FileType.Socket,
                            UnixFileMode.S_IFLNK => FileType.Unknown,
                            UnixFileMode.S_IFREG => FileType.File,
                            UnixFileMode.S_IFBLK => FileType.BlockDevice,
                            UnixFileMode.S_IFDIR => FileType.Folder,
                            UnixFileMode.S_IFCHR => FileType.CharDevice,
                            UnixFileMode.S_IFIFO => FileType.FIFO,
                            (UnixFileMode)0 => FileType.Unknown,
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
                    Data.ProgressRedirectionPath,
                    ADB_PATH,
                    cancellationToken,
                    Encoding.Unicode,
                    "-s",
                    ID,
                    opertation,
                    operationArgs,
                    EscapeAdbString(sourcePath),
                    EscapeAdbString(targetPath));

                // Each line should be a progress update (but sometimes the output can be weird)
                string lastStdoutLine = null;
                foreach (string stdoutLine in stdout)
                {
                    lastStdoutLine = stdoutLine;
                    var progressMatch = AdbRegEx.RE_FILE_SYNC_PROGRESS.Match(stdoutLine);
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

                var match = AdbRegEx.RE_FILE_SYNC_STATS.Match(lastStdoutLine);
                if (!match.Success)
                {
                    return null;
                }

                var path = match.Groups["TargetPath"].Value;
                UInt64 totalTransferred = UInt64.Parse(match.Groups["TotalTransferred"].Value);
                UInt64 totalSkipped = UInt64.Parse(match.Groups["TotalSkipped"].Value);
                decimal? avrageRate = match.Groups["AverageRate"].Success ? decimal.Parse(match.Groups["AverageRate"].Value) : null;
                UInt64? totalBytes = match.Groups["TotalBytes"].Success ? UInt64.Parse(match.Groups["TotalBytes"].Value) : null;
                decimal? totalTime = match.Groups["TotalTime"].Success ? decimal.Parse(match.Groups["TotalTime"].Value) : null;

                return new AdbSyncStatsInfo
                {
                    TargetPath = path,
                    FilesTransferred = totalTransferred,
                    FilesSkipped = totalSkipped,
                    AverageRate = avrageRate,
                    TotalBytes = totalBytes,
                    TotalTime = totalTime
                };
            }

            public string TranslateDevicePath(string path)
            {
                int exitCode = ExecuteDeviceAdbShellCommand(ID, "cd", out string stdout, out string stderr, EscapeAdbShellString(path), "&&", "pwd");
                if (exitCode != 0)
                {
                    throw new Exception(stderr);
                }
                return stdout.TrimEnd(LINE_SEPARATORS);
            }

            public string TranslateDeviceParentPath(string path) => TranslateDevicePath(ConcatPaths(path, PARENT_DIR));

            public List<Drive> GetDrives()
            {
                List<Drive> drives = new();

                var root = ReadDrives(AdbRegEx.RE_EMULATED_STORAGE_SINGLE, "/");
                if (root is null)
                    return null;
                else if (root.Any())
                    drives.Add(root.First());

                var intStorage = ReadDrives(AdbRegEx.RE_EMULATED_STORAGE_SINGLE, "/sdcard");
                if (intStorage is null)
                    return drives;
                else if (intStorage.Any())
                    drives.Add(intStorage.First());

                var extStorage = ReadDrives(AdbRegEx.RE_EMULATED_ONLY, EMULATED_DRIVES_GREP);
                if (extStorage is null)
                    return drives;
                else
                {
                    Func<Drive, bool> predicate = drives.Any(drive => drive.Type is AbstractDrive.DriveType.Internal)
                        ? d => d.Type is not AbstractDrive.DriveType.Internal or AbstractDrive.DriveType.Root
                        : d => d.Type is not AbstractDrive.DriveType.Root;
                    drives.AddRange(extStorage.Where(predicate));
                }

                if (!drives.Any(d => d.Type == AbstractDrive.DriveType.Internal))
                {
                    drives.Insert(0, new(path: AdbExplorerConst.DEFAULT_PATH));
                }

                if (!drives.Any(d => d.Type == AbstractDrive.DriveType.Root))
                {
                    drives.Insert(0, new(path: "/"));
                }

                return drives;
            }

            private IEnumerable<Drive> ReadDrives(Regex re, params string[] args)
            {
                int exitCode = ExecuteDeviceAdbShellCommand(ID, "df", out string stdout, out string stderr, args);
                if (exitCode != 0)
                    return null;

                return re.Matches(stdout).Select(m => new Drive(m.Groups, isEmulator: ID.Contains("emulator"), forcePath: args[0] == "/" ? "/" : ""));
            }

            private Dictionary<string, string> props;
            public Dictionary<string, string> Props
            {
                get
                {
                    if (props is null)
                    {
                        int exitCode = ExecuteDeviceAdbShellCommand(ID, GET_PROP, out string stdout, out string stderr);
                        if (exitCode == 0)
                        {
                            props = stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Where(
                                l => l[0] == '[' && l[^1] == ']').ToDictionary(
                                    line => line.Split(':')[0].Trim('[', ']', ' '),
                                    line => line.Split(':')[1].Trim('[', ']', ' '));
                        }
                    }
                    return props;
                }
            }

            public string MmcProp => Props.ContainsKey(MMC_PROP) ? Props[MMC_PROP] : null;
            public string OtgProp => Props.ContainsKey(OTG_PROP) ? Props[OTG_PROP] : null;

            public Task<string> GetAndroidVersion() => Task.Run(() =>
            {
                if (Props.ContainsKey(ANDROID_VERSION))
                    return Props[ANDROID_VERSION];
                else
                    return "";
            });

            public static Dictionary<string, string> GetBatteryInfo(LogicalDevice device)
            {
                if (ExecuteDeviceAdbShellCommand(device.ID, BATTERY, out string stdout, out string stderr) == 0)
                {
                    return stdout.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Where(l => l.Contains(':')).ToDictionary(
                        line => line.Split(':')[0].Trim(),
                        line => line.Split(':')[1].Trim());
                }
                return null;
            }

            public static void Reboot(LogicalDevice device, string arg)
            {
                if (ExecuteDeviceAdbCommand(device.ID, "reboot", out string stdout, out string stderr, arg) != 0)
                    throw new Exception(stderr);
            }

            public static bool GetDeviceIp(LogicalDevice device)
            {
                if (ExecuteDeviceAdbShellCommand(device.ID, "ip", out string stdout, out string stderr, new[] { "-f", "inet", "addr", "show", "wlan0" }) != 0)
                    return false;

                var match = AdbRegEx.RE_DEVICE_WLAN_INET.Match(stdout);
                if (!match.Success)
                    return false;

                device.IpAddress = match.Groups["IP"].Value;

                return true;
            }
        }
    }
}
