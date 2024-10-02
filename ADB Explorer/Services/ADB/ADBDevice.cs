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
        public LogicalDeviceViewModel Device { get; private set; } = other;

        public override string ID => Device.ID;

        public override DeviceType Type => Device.Type;

        public override DeviceStatus Status => Device.Status;
        
        public override string IpAddress => Device.IpAddress;

        private const string CURRENT_DIR = ".";
        private const string PARENT_DIR = "..";
        private static readonly string[] SPECIAL_DIRS = [CURRENT_DIR, PARENT_DIR];

        private enum UnixFileMode : UInt32
        {
            S_IFMT = 0b1111 << 12,   // bit mask for the file type bit fields
            S_IFSOCK = 0b1100 << 12, // socket
            S_IFLNK = 0b1010 << 12,  // symbolic link
            S_IFREG = 0b1000 << 12,  // regular file
            S_IFBLK = 0b0110 << 12,  // block device
            S_IFDIR = 0b0100 << 12,  // directory
            S_IFCHR = 0b0010 << 12,  // character device
            S_IFIFO = 0b0001 << 12   // FIFO
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
            var mode = UInt32.Parse(match.Groups["Mode"].Value, NumberStyles.HexNumber);

            if (SPECIAL_DIRS.Contains(name))
                return null;

            return new(
                fileName: name,
                path: FileHelper.ConcatPaths(path, name),
                type: ParseFileMode(mode),
                size: (mode != 0) ? size : new UInt64?(),
                modifiedTime: (time > 0) ? DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime() : new DateTime?(),
                isLink: (mode & (UInt32)UnixFileMode.S_IFMT) == (UInt32)UnixFileMode.S_IFLNK);
        }

        private static FileType ParseFileMode(uint mode) => 
            (UnixFileMode)(mode & (UInt32)UnixFileMode.S_IFMT) switch
        {
            UnixFileMode.S_IFSOCK => FileType.Socket,
            UnixFileMode.S_IFLNK => FileType.Unknown,
            UnixFileMode.S_IFREG => FileType.File,
            UnixFileMode.S_IFBLK => FileType.BlockDevice,
            UnixFileMode.S_IFDIR => FileType.Folder,
            UnixFileMode.S_IFCHR => FileType.CharDevice,
            UnixFileMode.S_IFIFO => FileType.FIFO,
            _ => FileType.Unknown,
        };

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
            var uniqueLinks = linkDict.Values.Distinct().Select(l => EscapeAdbShellString(l));

            // Get file mode of all unique links
            ExecuteDeviceAdbShellCommand(ID, "stat", out string statStdout, out string statStderr, cancellationToken, [.. uniqueLinks, .. STAT_LINKMODE_ARGS]);
            var modes = AdbRegEx.RE_LINK_MODE().Matches(statStdout);

            // Prepare a target->mode dictionary, where the RegEx matches
            var linkTypes = modes.Where(match => match.Success).ToDictionary(
                match => match.Groups["Target"].Value,
                match => ParseFileMode(UInt32.Parse(match.Groups["Mode"].Value, NumberStyles.HexNumber)));

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
                dispatcher.Invoke(() => DialogService.ShowMessage(Strings.S_LS_ERROR(e), Strings.S_LS_ERROR_TITLE, DialogService.DialogIcon.Critical, true, copyToClipboard: true));
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
            string workingDir = "", _target = target;
            if (target[1] == ':')
            {
                workingDir = FileHelper.GetParentPath(target);
                _target = target.Split('\\').Count(s => s.Length > 0) < 2 ? "./" : FileHelper.GetFullName(target);
            }

            _target = EscapeAdbString(_target);

            var stdout = RedirectCommandAsync(
                Data.RuntimeSettings.AdbPath,
                cancellationToken,
                cmdProcess,
                workingDir,
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

            int exitCode = ExecuteDeviceAdbShellCommand(ID, "cd", out string stdout, out string stderr, new(), EscapeAdbShellString(path), "&&", "pwd");
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
            else if (intStorage.Any())
                drives.Add(intStorage.First());

            var extStorage = ReadDrives(AdbRegEx.RE_EMULATED_ONLY(), EMULATED_DRIVES_GREP);
            if (extStorage is null)
                return drives;
            else
            {
                Func<LogicalDrive, bool> predicate = drives.Any(drive => drive.Type is AbstractDrive.DriveType.Internal)
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

        private IEnumerable<LogicalDrive> ReadDrives(Regex re, params string[] args)
        {
            int exitCode = ExecuteDeviceAdbShellCommand(ID, "df", out string stdout, out string stderr, new(), args);
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
                    int exitCode = ExecuteDeviceAdbShellCommand(ID, GET_PROP, out string stdout, out string stderr, new());
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

        public string MmcProp => Props.TryGetValue(MMC_PROP, out string value) ? value : null;
        public string OtgProp => Props.TryGetValue(OTG_PROP, out string value) ? value : null;

        public Task<string> GetAndroidVersion() => Task.Run(() =>
        {
            if (Props.TryGetValue(ANDROID_VERSION, out string value))
                return value;
            else
                return "";
        });

        public static Dictionary<string, string> GetBatteryInfo(LogicalDevice device)
        {
            if (ExecuteDeviceAdbShellCommand(device.ID, BATTERY, out string stdout, out string stderr, new()) == 0)
            {
                return stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries).Where(l => l.Contains(':')).ToDictionary(
                    line => line.Split(':')[0].Trim(),
                    line => line.Split(':')[1].Trim());
            }
            return null;
        }

        public static void Reboot(string deviceId, string arg)
        {
            if (ExecuteDeviceAdbCommand(deviceId, "reboot", out string stdout, out string stderr, new(), arg) != 0)
                throw new Exception(string.IsNullOrEmpty(stderr) ? stdout : stderr);
        }

        public static bool GetDeviceIp(DeviceViewModel device)
        {
            if (ExecuteDeviceAdbShellCommand(device.ID, "ip", out string stdout, out _, new(), INET_ARGS) != 0)
                return false;

            var match = AdbRegEx.RE_DEVICE_WLAN_INET().Match(stdout);
            if (!match.Success)
                return false;

            device.SetIpAddress(match.Groups["IP"].Value);

            return true;
        }
    }
}
