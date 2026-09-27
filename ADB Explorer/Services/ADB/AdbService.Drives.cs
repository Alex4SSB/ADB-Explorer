using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.AdbRegEx;

namespace ADB_Explorer.Services;

public partial class AdbService
{
    public readonly record struct DriveMountInfo(AbstractDrive.DriveType Type, string Manufacturer, string VolumeLabel);

    /// <summary>
    /// Classifies every currently mounted removable drive as an SD/expansion card or a USB/OTG
    /// drive, and captures the same manufacturer + volume label Android's own storage settings
    /// show, by correlating <c>dumpsys mount</c>'s Disks section (<c>flags=SD|USB</c>, <c>label=</c>)
    /// with its Volumes section (<c>diskId=</c>, <c>fsLabel=</c>, <c>path=</c>). Keyed by mount path.
    /// </summary>
    public static Dictionary<string, DriveMountInfo> GetRemovableDriveInfo(string deviceID)
    {
        var exitCode = ExecuteDeviceAdbShellCommand(deviceID, "dumpsys", out string stdout, out _, CancellationToken.None, "mount");

        return exitCode == 0 ? ParseMountDump(stdout) : [];
    }

    internal static Dictionary<string, DriveMountInfo> ParseMountDump(string stdout)
    {
        Dictionary<string, (bool IsSd, string Label)> disks = [];
        foreach (Match disk in RE_DUMPSYS_MOUNT_DISK().Matches(stdout))
        {
            disks[disk.Groups["Disk"].Value] = (disk.Groups["Flags"].Value.Contains("SD"), disk.Groups["Label"].Value.Trim());
        }

        Dictionary<string, DriveMountInfo> volumes = [];
        foreach (Match volume in RE_DUMPSYS_MOUNT_VOLUME().Matches(stdout))
        {
            if (!disks.TryGetValue(volume.Groups["Disk"].Value, out var disk))
                continue;

            var type = disk.IsSd ? AbstractDrive.DriveType.Expansion : AbstractDrive.DriveType.External;
            volumes[volume.Groups["Path"].Value] = new(type, disk.Label, volume.Groups["FsLabel"].Value.Trim());
        }

        return volumes;
    }

    private static readonly string DRIVE_POLL_ROOT = $"{ADB_UNIT_SEP}ROOT{ADB_UNIT_SEP}";

    private static readonly string DRIVE_POLL_SDCARD = $"{ADB_UNIT_SEP}SDCARD{ADB_UNIT_SEP}";

    private static readonly string DRIVE_POLL_EXT = $"{ADB_UNIT_SEP}EXT{ADB_UNIT_SEP}";

    private static readonly string DRIVE_POLL_TEMP = $"{ADB_UNIT_SEP}TEMP{ADB_UNIT_SEP}";

    private static readonly string DRIVE_POLL_PKG = $"{ADB_UNIT_SEP}PKG{ADB_UNIT_SEP}";

    private static readonly string DRIVE_POLL_TRASH = $"{ADB_UNIT_SEP}TRASH{ADB_UNIT_SEP}";

    private static readonly string DRIVE_POLL_TRASH_EXISTS = $"{ADB_UNIT_SEP}TRASH_EXISTS{ADB_UNIT_SEP}";

    private static readonly string DRIVE_POLL_APK = $"{ADB_UNIT_SEP}APK{ADB_UNIT_SEP}";

    /// <summary>
    /// Polls drive view data in a single <c>adb shell</c> round trip (df + optional item counts).
    /// </summary>
    public static DrivePollResult? GetDrives(
        string deviceId,
        DeviceType deviceType,
        CancellationToken cancellationToken,
        bool countRecycle = false,
        bool countPackages = false,
        bool countInstallers = false,
        bool includeSystemPackages = true)
    {
        var script = BuildDrivePollScript(countRecycle, countPackages, countInstallers, includeSystemPackages);
        int exitCode = ExecuteDeviceAdbShellCommand(deviceId, script, out string stdout, out _, cancellationToken);
        if (exitCode != 0 && string.IsNullOrWhiteSpace(stdout))
            return null;

        return ParseDrivePollOutput(stdout, deviceType, countRecycle, countPackages, countInstallers);
    }

    private static string BuildDrivePollScript(bool countRecycle, bool countPackages, bool countInstallers, bool includeSystemPackages)
    {
        List<(string Mark, string Command)> sections =
        [
            (DRIVE_POLL_ROOT, "df /"),
            (DRIVE_POLL_SDCARD, "df /sdcard"),
            (DRIVE_POLL_EXT, "df | grep -E '/mnt/media_rw/|/storage/'"),
            (DRIVE_POLL_TEMP, $"df {TEMP_PATH}"),
        ];

        if (countPackages)
        {
            // -3 = third-party only; omit for all packages (matches GetPackages includeSystem).
            var listPackages = includeSystemPackages ? "pm list packages" : "pm list packages -3";
            sections.Add((DRIVE_POLL_PKG, $"{listPackages} | wc -l"));
        }

        if (countRecycle)
        {
            sections.Add((DRIVE_POLL_TRASH, BuildFindCountCommand(RECYCLE_PATH, excludeNames: ["*" + RECYCLE_INDEX_SUFFIX])));
            sections.Add((DRIVE_POLL_TRASH_EXISTS, $"[ -d {EscapeAdbShellString(RECYCLE_PATH)} ] && echo 1 || echo 0"));
        }

        if (countInstallers)
            sections.Add((DRIVE_POLL_APK, BuildFindCountCommand(TEMP_PATH, includeNames: INSTALL_APK.Select(name => "*" + name))));

        List<string> parts = [];
        for (var i = 0; i < sections.Count; i++)
        {
            var (mark, command) = sections[i];
            // First mark alone; later boundaries merge separator + next mark into one echo.
            parts.Add(i == 0 ? $"echo {mark}" : $"echo {ADB_FIELD_SEP}{mark}");
            parts.Add(command);
        }

        parts.Add($"echo {ADB_FIELD_SEP}");
        return string.Join("; ", parts);
    }

    internal static DrivePollResult ParseDrivePollOutput(
        string stdout,
        DeviceType deviceType,
        bool countRecycle,
        bool countPackages,
        bool countInstallers)
    {
        List<DriveSnapshot> drives = [];

        var root = ParseDriveSection(ExtractDrivePollSection(stdout, DRIVE_POLL_ROOT), deviceType, RE_EMULATED_STORAGE_SINGLE(), "/");
        if (root.Count > 0)
            drives.Add(root[0]);

        var intStorage = ParseDriveSection(ExtractDrivePollSection(stdout, DRIVE_POLL_SDCARD), deviceType, RE_EMULATED_STORAGE_SINGLE(), "");
        if (intStorage.Count > 0)
            drives.Add(intStorage[0]);

        var extStorage = ParseDriveSection(ExtractDrivePollSection(stdout, DRIVE_POLL_EXT), deviceType, RE_EMULATED_ONLY(), "");

        Func<DriveSnapshot, bool> predicate = drives.Any(d => d.Type is AbstractDrive.DriveType.Internal)
            ? d => d.Type is not AbstractDrive.DriveType.Internal and not AbstractDrive.DriveType.Root
            : d => d.Type is not AbstractDrive.DriveType.Root;

        drives.AddRange(extStorage.Where(predicate));

        var tempStorage = ParseDriveSection(ExtractDrivePollSection(stdout, DRIVE_POLL_TEMP), deviceType, RE_EMULATED_STORAGE_SINGLE(), "");
        if (tempStorage.Count > 0)
            drives.Add(tempStorage[0] with { Type = AbstractDrive.DriveType.Temp });

        if (drives.All(d => d.Type != AbstractDrive.DriveType.Internal))
            drives.Insert(0, new(Path: "/sdcard", Type: AbstractDrive.DriveType.Internal, Size: "", Used: "", Available: "", UsageP: -1, FileSystem: "", IsEmulator: false));

        if (drives.All(d => d.Type != AbstractDrive.DriveType.Root))
            drives.Insert(0, new(Path: "/", Type: AbstractDrive.DriveType.Root, Size: "", Used: "", Available: "", UsageP: -1, FileSystem: "", IsEmulator: false));

        long? recycleCount = null;
        ulong? packagesCount = null;
        ulong? installersCount = null;

        if (countRecycle)
        {
            var rawCount = ParseUlongSection(ExtractDrivePollSection(stdout, DRIVE_POLL_TRASH));
            var trashExists = ExtractDrivePollSection(stdout, DRIVE_POLL_TRASH_EXISTS).Trim() == "1";
            recycleCount = rawCount < 1
                ? trashExists ? 0 : -1
                : (long)rawCount;
        }

        if (countPackages)
            packagesCount = ParseUlongSection(ExtractDrivePollSection(stdout, DRIVE_POLL_PKG));

        if (countInstallers)
            installersCount = ParseUlongSection(ExtractDrivePollSection(stdout, DRIVE_POLL_APK));

        return new(drives, recycleCount, packagesCount, installersCount);
    }

    private static List<DriveSnapshot> ParseDriveSection(string section, DeviceType deviceType, Regex re, string forcePath)
    {
        if (string.IsNullOrWhiteSpace(section))
            return [];

        // RE_EMULATED_* require a trailing newline on each matched line.
        if (!section.EndsWith('\n'))
            section += "\n";

        return [.. re.Matches(section).Select(m => DriveSnapshot.Parse(m.Groups, isEmulator: deviceType is DeviceType.Emulator, forcePath: forcePath))];
    }

    private static ulong ParseUlongSection(string section)
        => ulong.TryParse(section.Trim(LINE_SEPARATORS), out var value) ? value : 0;

    private static string ExtractDrivePollSection(string stdout, string label)
    {
        var start = stdout.IndexOf(label, StringComparison.Ordinal);
        if (start < 0)
            return "";

        var end = stdout.IndexOf(ADB_FIELD_SEP, start + label.Length);
        if (end < 0)
            end = stdout.Length;

        return stdout[(start + label.Length)..end].Trim(ADB_FIELD_SEP, ' ', '\r', '\n');
    }
}
