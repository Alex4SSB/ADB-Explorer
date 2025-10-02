using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Resources;
using ADB_Explorer.ViewModels;
using static ADB_Explorer.Models.AbstractFile;

namespace ADB_Explorer.Services;

public partial class ADBService
{
    public static readonly char[] LINE_SEPARATORS = ['\n', '\r'];

    private const string GET_PROP = "getprop";
    private const string ANDROID_VERSION = "ro.build.version.release";
    private const string BATTERY = "dumpsys battery";
    private const string MMC_PROP = "vold.microsd.uuid";
    private const string OTG_PROP = "vold.otgstorage.uuid";

    // First partition of MMC block device 0 / 1
    private static readonly string[] MMC_BLOCK_DEVICES = ["/dev/block/mmcblk0p1", "/dev/block/mmcblk1p1"];
    private static readonly string[] EMULATED_DRIVES_GREP = ["|", "grep", "-E", "'/mnt/media_rw/|/storage/'"];

    private static readonly string[] READLINK_ARGS1 = ["link", "in"]; // Preceded by 'for'
    private static readonly string[] READLINK_ARGS2 = [";", "do", "target=$(readlink", "-f", "$link", "2>&1);", "echo", "/// $link /// $target ///;", "done"];
    private static readonly string[] STAT_LINKMODE_ARGS = ["-c", "'/// %n /// %f ///'", "2>&1"];

    private static readonly string[] INET_ARGS = ["-f", "inet", "addr", "show", "wlan0"];

    public class AdbDevice(LogicalDeviceViewModel other) : Device
    {
        public LogicalDeviceViewModel Device { get; } = other;

        public override string ID => Device.ID;

        public override DeviceType Type => Device.Type;

        public override DeviceStatus Status => Device.Status;
        
        public override string IpAddress => Device.IpAddress;

        private const string CURRENT_DIR = ".";
        private const string PARENT_DIR = "..";
        private static readonly string[] SPECIAL_DIRS = [CURRENT_DIR, PARENT_DIR];

        /// <summary>Represents the Unix filesystem permissions and file type.<br />
        /// Since the file type flags overlap, they CANNOT be used as flags.</summary>
        public enum UnixFileMode
        {
            /// <summary>No permissions.</summary>
            None = 0x0,
            /// <summary>Execute permission for others.</summary>
            OtherExecute = 0x1,
            /// <summary>Write permission for others.</summary>
            OtherWrite = 0x2,
            /// <summary>Read permission for others.</summary>
            OtherRead = 0x4,
            /// <summary>Execute permission for group.</summary>
            GroupExecute = 0x8,
            /// <summary>Write permission for group.</summary>
            GroupWrite = 0x10,
            /// <summary>Read permission for group.</summary>
            GroupRead = 0x20,
            /// <summary>Execute permission for owner.</summary>
            UserExecute = 0x40,
            /// <summary>Write permission for owner.</summary>
            UserWrite = 0x80,
            /// <summary>Read permission for owner.</summary>
            UserRead = 0x100,
            /// <summary>Sticky bit permission.</summary>
            StickyBit = 0x200,
            /// <summary>Set group permission.</summary>
            SetGroup = 0x400,
            /// <summary>Set user permission.</summary>
            SetUser = 0x800,
            /// <summary>FIFO.</summary>
            S_IFIFO = 0x1000,
            /// <summary>Character device.</summary>
            S_IFCHR = 0x2000,
            /// <summary>Directory.</summary>
            S_IFDIR = 0x4000,
            /// <summary>Block device.</summary>
            S_IFBLK = 0x6000,
            /// <summary>Regular file.</summary>
            S_IFREG = 0x8000,
            /// <summary>Symbolic link.</summary>
            S_IFLNK = 0xA000,
            /// <summary>Socket.</summary>
            S_IFSOCK = 0xC000,
        }

        private static FileStat CreateFile(string path, string stdoutLine)
        {
            var match = AdbRegEx.RE_LS_FILE_ENTRY().Match(stdoutLine);
            if (!match.Success)
            {
                throw new Exception($"Invalid output for adb ls command: {stdoutLine}");
            }

            var name = match.Groups["Name"].Value;
            var size = UInt64.Parse(match.Groups["Size"].Value, NumberStyles.HexNumber);
            var time = long.Parse(match.Groups["Time"].Value, NumberStyles.HexNumber);
            var mode = (UnixFileMode)UInt32.Parse(match.Groups["Mode"].Value, NumberStyles.HexNumber);

            if (SPECIAL_DIRS.Contains(name))
                return null;

            return new(
                fileName: name,
                path: FileHelper.ConcatPaths(path, name),
                type: ParseFileMode(mode),
                size: (mode != 0) ? size : null,
                modifiedTime: (time > 0) ? DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime() : null,
                isLink: mode.HasFlag(UnixFileMode.S_IFLNK));
        }

        private static FileType ParseFileMode(UnixFileMode mode)
        {
            if (mode.HasFlag(UnixFileMode.S_IFSOCK)) return FileType.Socket;
            if (mode.HasFlag(UnixFileMode.S_IFLNK)) return FileType.Unknown;
            if (mode.HasFlag(UnixFileMode.S_IFREG)) return FileType.File;
            if (mode.HasFlag(UnixFileMode.S_IFBLK)) return FileType.BlockDevice;
            if (mode.HasFlag(UnixFileMode.S_IFDIR)) return FileType.Folder;
            if (mode.HasFlag(UnixFileMode.S_IFCHR)) return FileType.CharDevice;
            if (mode.HasFlag(UnixFileMode.S_IFIFO)) return FileType.FIFO;

            return FileType.Unknown;
        }

        public IEnumerable<(string, FileType)> GetLinkType(IEnumerable<string> filePaths, CancellationToken cancellationToken)
        {
            // Run readlink in a loop to support single param version
            ExecuteDeviceAdbShellCommand(ID,
                                         "for",
                                         out string stdout,
                                         out string stderr,
                                         cancellationToken,
                                         [.. READLINK_ARGS1, .. filePaths.Select(f => EscapeAdbShellString(f)), .. READLINK_ARGS2]);

            // Prepare a link->target dictionary, where the RegEx matched
            var links = AdbRegEx.RE_LINK_TARGETS().Matches(stdout);
            var linkDict = links.Where(match => match.Success).ToDictionary(match => match.Groups["Source"].Value, match => match.Groups["Target"].Value);
            var uniqueLinks = linkDict.Values.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().Select(l => EscapeAdbShellString(l));

            // Get file mode of all unique links
            ExecuteDeviceAdbShellCommand(ID, "stat", out string statStdout, out string statStderr, cancellationToken, [.. uniqueLinks, .. STAT_LINKMODE_ARGS]);
            var modes = AdbRegEx.RE_LINK_MODE().Matches(statStdout);

            // Prepare a target->mode dictionary, where the RegEx matches
            var linkTypes = modes.Where(match => match.Success).ToDictionary(
                match => match.Groups["Target"].Value,
                match => ParseFileMode((UnixFileMode)UInt32.Parse(match.Groups["Mode"].Value, NumberStyles.HexNumber)));

            // Iterate over input files using the dictionaries
            foreach (var file in filePaths)
            {
                if (linkDict.TryGetValue(file, out var target))
                {
                    if (linkTypes.TryGetValue(target, out var type))
                    {
                        yield return (target, type);
                        continue;
                    }

                    yield return (target, FileType.BrokenLink);
                    continue;
                }
                
                yield return ("", FileType.Unknown);
            }
        }

        public void ListDirectory(string path, ref ConcurrentQueue<FileStat> output, Dispatcher dispatcher, CancellationToken cancellationToken)
        {
            IEnumerable<string> stdout;

            try
            {
                stdout = ExecuteDeviceAdbCommandAsync(ID, "ls", cancellationToken, EscapeAdbString(path));

                foreach (string stdoutLine in stdout)
                {
                    var item = CreateFile(path, stdoutLine);

                    if (item is null)
                        continue;

                    output.Enqueue(item);
                }
            }
            catch (Exception e)
            {
                var message = e.Message;
                if (!string.IsNullOrEmpty(message))
                    message += "\n\n";
                
                dispatcher.Invoke(() => DialogService.ShowMessage(message + Strings.Resources.S_LS_ERROR,
                                                                  Strings.Resources.S_LS_ERROR_TITLE,
                                                                  DialogService.DialogIcon.Critical,
                                                                  true,
                                                                  copyToClipboard: true));
                return;
            }
        }

        public AdbSyncStatsInfo DoFileSync(
            string cmd,
            string arg,
            string target,
            string source,
            Process cmdProcess,
            ref ObservableList<FileOpProgressInfo> updates, CancellationToken cancellationToken)
        {
            string _target = target;

            if (target[1] == ':' && target.Count(c => c == '\\') < 2)
                _target = FileHelper.ConcatPaths(target, ".", '\\');

            _target = EscapeAdbString(_target);

            var stdout = RedirectCommandAsync(
                Data.RuntimeSettings.AdbPath,
                cancellationToken,
                cmdProcess,
                args: [
                    "-s",
                    ID,
                    cmd,
                    arg,
                    EscapeAdbString(source),
                    _target
                ]);
            
            // Each line should be a progress update (but sometimes the output can be weird)
            string lastStdoutLine = null;
            foreach (string stdoutLine in stdout)
            {
                lastStdoutLine = stdoutLine;
                if (string.IsNullOrWhiteSpace(lastStdoutLine))
                    continue;

                var progressMatch = AdbRegEx.RE_FILE_SYNC_PROGRESS().Match(stdoutLine);
                if (progressMatch.Success)
                    updates.Add(new AdbSyncProgressInfo(progressMatch));
                else
                {
                    var errorMatch = AdbRegEx.RE_FILE_SYNC_ERROR().Match(stdoutLine);
                    if (errorMatch.Success && SyncErrorInfo.New(errorMatch) is SyncErrorInfo error)
                    {
                        updates.Add(error);
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(lastStdoutLine))
                return null;

            var match = AdbRegEx.RE_FILE_SYNC_STATS().Match(lastStdoutLine);
            if (!match.Success)
                return null;

            return new AdbSyncStatsInfo(match);
        }

        public string TranslateDevicePath(string path)
        {
            if (path.StartsWith('~'))
                path = path.Length == 1 ? "/" : path[1..];

            if (path.StartsWith("//"))
                path = path[1..];

            int exitCode = ExecuteDeviceAdbShellCommand(ID, "cd", out string stdout, out string stderr, CancellationToken.None, EscapeAdbShellString(path), "&&", "pwd");
            if (exitCode != 0)
            {
                throw new Exception(stderr);
            }
            return stdout.TrimEnd(LINE_SEPARATORS);
        }

        public List<LogicalDrive> GetDrives()
        {
            List<LogicalDrive> drives = [];

            var root = ReadDrives(AdbRegEx.RE_EMULATED_STORAGE_SINGLE(), "/");
            if (root is null)
                return null;
            else if (root.Any())
                drives.Add(root.First());

            var intStorage = ReadDrives(AdbRegEx.RE_EMULATED_STORAGE_SINGLE(), "/sdcard");
            if (intStorage is null)
                return drives;
            if (intStorage.Any())
                drives.Add(intStorage.First());

            var extStorage = ReadDrives(AdbRegEx.RE_EMULATED_ONLY(), EMULATED_DRIVES_GREP);
            if (extStorage is null)
                return drives;

            Func<LogicalDrive, bool> predicate = drives.Any(drive => drive.Type is AbstractDrive.DriveType.Internal)
                ? d => d.Type is not AbstractDrive.DriveType.Internal and not AbstractDrive.DriveType.Root
                : d => d.Type is not AbstractDrive.DriveType.Root;

            drives.AddRange(extStorage.Where(predicate));

            if (drives.All(d => d.Type != AbstractDrive.DriveType.Internal))
            {
                drives.Insert(0, new(path: AdbExplorerConst.DEFAULT_PATH));
            }

            if (drives.All(d => d.Type != AbstractDrive.DriveType.Root))
            {
                drives.Insert(0, new(path: "/"));
            }

            return drives;
        }

        private IEnumerable<LogicalDrive> ReadDrives(Regex re, params string[] args)
        {
            int exitCode = ExecuteDeviceAdbShellCommand(ID, "df", out string stdout, out string stderr, CancellationToken.None, args);
            if (exitCode != 0)
                return null;

            return re.Matches(stdout).Select(m => new LogicalDrive(m.Groups, isEmulator: Type is DeviceType.Emulator, forcePath: args[0] == "/" ? "/" : ""));
        }

        private Dictionary<string, string> props;
        public Dictionary<string, string> Props
        {
            get
            {
                if (props is null)
                {
                    int exitCode = ExecuteDeviceAdbShellCommand(ID, GET_PROP, out string stdout, out string stderr, CancellationToken.None);
                    if (exitCode == 0)
                    {
                        props = stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Where(
                            l => l[0] == '[' && l[^1] == ']').TryToDictionary(
                                line => line.Split(':')[0].Trim('[', ']', ' '),
                                line => line.Split(':')[1].Trim('[', ']', ' '));
                    }
                    else
                        props = [];

                }

                return props;
            }
        }

        public string MmcProp => Props.GetValueOrDefault(MMC_PROP);
        public string OtgProp => Props.GetValueOrDefault(OTG_PROP);

        public Task<string> GetAndroidVersion() => Task.Run(() => Props.GetValueOrDefault(ANDROID_VERSION, ""));

        public static Dictionary<string, string> GetBatteryInfo(LogicalDevice device)
        {
            if (ExecuteDeviceAdbShellCommand(device.ID, BATTERY, out string stdout, out string stderr, CancellationToken.None) == 0)
            {
                return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Where(l => l.Contains(':')).ToDictionary(
                    line => line.Split(':')[0].Trim(),
                    line => line.Split(':')[1].Trim());
            }
            return null;
        }

        public static void Reboot(string deviceId, string arg)
        {
            if (ExecuteDeviceAdbCommand(deviceId, "reboot", out string stdout, out string stderr, CancellationToken.None, arg) != 0)
                throw new Exception(string.IsNullOrEmpty(stderr) ? stdout : stderr);
        }

        public static bool GetDeviceIp(DeviceViewModel device)
        {
            if (ExecuteDeviceAdbShellCommand(device.ID, "ip", out string stdout, out _, CancellationToken.None, INET_ARGS) != 0)
                return false;

            var match = AdbRegEx.RE_DEVICE_WLAN_INET().Match(stdout);
            if (!match.Success)
                return false;

            device.SetIpAddress(match.Groups["IP"].Value);

            return true;
        }

        public static bool ForceMediaScan(LogicalDeviceViewModel device)
        {
            // content call --method scan_volume --uri content://media --arg external_primary
            var res = ExecuteDeviceAdbShellCommand(device.ID,
                "content",
                out _,
                out _,
                CancellationToken.None,
                "call --method scan_volume",
                "--uri content://media",
                "--arg external_primary");
        
            return res == 0;
        }
    }
}
