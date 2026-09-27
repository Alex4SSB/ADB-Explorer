using static ADB_Explorer.Models.AbstractFile;
using static ADB_Explorer.Models.AdbExplorerConst;
using static ADB_Explorer.Models.AdbRegEx;

namespace ADB_Explorer.Services;

public partial class AdbService
{
    /// <summary>
    /// Returns the size of a device path via <c>stat -c%s</c>, or <see langword="null"/> if it cannot be read.
    /// </summary>
    public static long? TryGetFileSize(string deviceId, string path, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(path))
            return null;

        var exit = ExecuteDeviceAdbShellCommand(
            deviceId,
            "stat",
            out var stdout,
            out _,
            cancellationToken,
            "-c%s",
            EscapeAdbShellString(path));

        if (exit != 0 || !long.TryParse(stdout.Trim(), out var size) || size < 0)
            return null;

        return size;
    }

    public static LocationInfo? GetLocationInfo(string deviceId, string path, CancellationToken cancellationToken)
    {
        var stat = ShellCommands.TranslateCommand("stat");
        var escapedPath = EscapeAdbShellString(path);
        var statFormat = $"%U{ADB_FIELD_SEP}%G{ADB_FIELD_SEP}%u{ADB_FIELD_SEP}%g{ADB_FIELD_SEP}%a{ADB_FIELD_SEP}%x{ADB_FIELD_SEP}%y";
        var script =
            $"p={escapedPath};" +
            $"{stat} -c {EscapeAdbShellString(statFormat)} \"$p\";" +
            "r=0;w=0;x=0;test -r \"$p\"&&r=1;test -w \"$p\"&&w=1;test -x \"$p\"&&x=1;" +
            $"echo {ShellAccessHelper.AccessMarker}${{r}}${{w}}${{x}}";

        if (ExecuteDeviceAdbShellCommand(deviceId, script, out string stdout, out _, cancellationToken) != 0
            || string.IsNullOrWhiteSpace(stdout))
        {
            return null;
        }

        return ShellAccessHelper.ParseLocationInfo(stdout);
    }

    public static DevicePathKind? TryGetPathKind(string deviceId, string path, CancellationToken cancellationToken = default)
    {
        if (!ShellCommands.StatExists(deviceId))
            return null;

        var stat = ShellCommands.TranslateCommand("stat");
        var exitCode = ExecuteDeviceAdbShellCommand(
            deviceId,
            stat,
            out string stdout,
            out _,
            cancellationToken,
            "-c",
            "%F",
            EscapeAdbShellString(path));

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout))
            return null;

        return stdout.Trim() switch
        {
            "regular file" => DevicePathKind.RegularFile,
            "directory" => DevicePathKind.Directory,
            _ => DevicePathKind.Unknown,
        };
    }

    private static readonly SemaphoreSlim ExtraInfoStatGate = new(1, 1);

    private static readonly string[] STAT_LINKMODE_ARGS = ["-c", $"%n{ADB_FIELD_SEP}%f", "2>&1"];

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

    private static FileStat? CreateFile(string path, string stdoutLine)
    {
        var match = RE_LS_FILE_ENTRY().Match(stdoutLine);
        if (!match.Success)
        {
            throw new Exception($"Invalid output for adb ls command: {stdoutLine}");
        }

        var name = match.Groups["Name"].Value;
        long? size = long.Parse(match.Groups["Size"].Value, NumberStyles.HexNumber);
        var time = long.Parse(match.Groups["Time"].Value, NumberStyles.HexNumber);
        var mode = (UnixFileMode)UInt32.Parse(match.Groups["Mode"].Value, NumberStyles.HexNumber);

        if (SPECIAL_DIRS.Contains(name))
            return null;

        var type = ParseFileMode(mode);
        if (mode is UnixFileMode.None || type is FileType.Folder)
        {
            size = null;
        }

        UnixFileMode? permissions = mode is UnixFileMode.None 
            ? null 
            : mode & (UnixFileMode)511;

        return new(
            FullName: name,
            FullPath: FileHelper.ConcatPaths(path, name),
            Type: type,
            IsLink: mode.HasFlag(UnixFileMode.S_IFLNK),
            Size: size,
            ModifiedTime: (time > 0) ? DateTimeOffset.FromUnixTimeSeconds(time).DateTime.ToLocalTime() : null,
            Permissions: (System.IO.UnixFileMode?)permissions);
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

    public static async Task<FileExtraInfo?> GetFileExtraInfoAsync(string deviceId, string path, CancellationToken cancellationToken)
    {
        await ExtraInfoStatGate.WaitAsync(cancellationToken);
        try
        {
            if (cancellationToken.IsCancellationRequested)
                return null;

            return await Task.Run<FileExtraInfo?>(() =>
            {
                // Size, user, group, uid, gid, mode, access/modified (human-readable for UTC offset).
                // No %z: that is the status change time, which any rename or chmod moves - not a creation time.
                var res = ExecuteDeviceAdbShellCommand(deviceId,
                                                       "stat",
                                                       out string stdout,
                                                       out _,
                                                       cancellationToken,
                                                       "-c",
                                                       $"%s{ADB_FIELD_SEP}%U{ADB_FIELD_SEP}%G{ADB_FIELD_SEP}%u{ADB_FIELD_SEP}%g{ADB_FIELD_SEP}%a{ADB_FIELD_SEP}%x{ADB_FIELD_SEP}%y",
                                                       EscapeAdbShellString(path));

                if (res != 0 || string.IsNullOrWhiteSpace(stdout))
                    return null;

                try
                {
                    var parts = stdout.Split(ADB_FIELD_SEP);
                    long? size = long.TryParse(parts[0].Trim(), out long bytes) ? bytes : null;
                    int? ownerUid = int.TryParse(parts[3].Trim(), out var uid) ? uid : null;
                    int? ownerGid = int.TryParse(parts[4].Trim(), out var gid) ? gid : null;
                    var permissions = (System.IO.UnixFileMode)Convert.ToInt32(parts[5].Trim(), 8);

                    return new FileExtraInfo(
                        parts[1].Trim(),
                        parts[2].Trim(),
                        permissions,
                        DateTimeOffset.Parse(parts[6].Trim(), CultureInfo.InvariantCulture),
                        DateTimeOffset.Parse(parts[7].Trim(), CultureInfo.InvariantCulture),
                        size,
                        ownerUid,
                        ownerGid);
                }
                catch
                {
                    return null;
                }
            }, cancellationToken);
        }
        finally
        {
            ExtraInfoStatGate.Release();
        }
    }

    public static (IReadOnlyList<string> Users, IReadOnlyList<string> Groups) GetKnownUsersAndGroups(string deviceId, CancellationToken cancellationToken = default)
    {
        var cat = ShellCommands.TranslateCommand("cat");
        var echo = ShellCommands.TranslateCommand("echo");
        var script =
            $"{cat} {string.Join(' ', AndroidAids.PasswdPaths)} 2>/dev/null; " +
            $"{echo} {ADB_FIELD_SEP}; " +
            $"{cat} {string.Join(' ', AndroidAids.GroupPaths)} 2>/dev/null";

        ExecuteDeviceAdbShellCommand(deviceId, script, out string stdout, out _, cancellationToken);

        string passwdStdout;
        string groupStdout;
        var separator = stdout.IndexOf(ADB_FIELD_SEP);
        if (separator < 0)
        {
            passwdStdout = stdout;
            groupStdout = "";
        }
        else
        {
            passwdStdout = stdout[..separator];
            groupStdout = stdout[(separator + 1)..];
        }

        return (
            ShellAccessHelper.CombineKnownIdentities(passwdStdout),
            ShellAccessHelper.CombineKnownIdentities(groupStdout));
    }

    public static Task<string> ChangeFileModeAsync(string deviceId, string path, System.IO.UnixFileMode mode, CancellationToken cancellationToken)
        => ExecuteVoidShellCommand(deviceId, cancellationToken, "chmod", ShellAccessHelper.ToChmodOctal(mode), EscapeAdbShellString(path));

    public static Task<string> ChangeFileUserAsync(string deviceId, string path, string user, bool noDereference, CancellationToken cancellationToken)
    {
        if (noDereference)
            return ExecuteVoidShellCommand(deviceId, cancellationToken, "chown", "-h", EscapeAdbShellString(user), EscapeAdbShellString(path));

        return ExecuteVoidShellCommand(deviceId, cancellationToken, "chown", EscapeAdbShellString(user), EscapeAdbShellString(path));
    }

    public static Task<string> ChangeFileGroupAsync(string deviceId, string path, string group, bool noDereference, CancellationToken cancellationToken)
    {
        if (noDereference)
            return ExecuteVoidShellCommand(deviceId, cancellationToken, "chgrp", "-h", EscapeAdbShellString(group), EscapeAdbShellString(path));

        return ExecuteVoidShellCommand(deviceId, cancellationToken, "chgrp", EscapeAdbShellString(group), EscapeAdbShellString(path));
    }

    public static IEnumerable<(string, FileType)> GetLinkType(string deviceId, IEnumerable<string> filePaths, CancellationToken cancellationToken)
    {
        var pathList = filePaths as IList<string> ?? [.. filePaths];
        if (pathList.Count == 0)
            yield break;

        var echo = ShellCommands.TranslateCommand("echo");
        var readlink = ShellCommands.TranslateCommand("readlink");

        // Inline each path in readlink/echo; a for-loop $link loses quotes through adb/Windows parsing.
        var readlinkScript = string.Join("; ", pathList.Select(path =>
            $"{echo} {EscapeAdbShellString(path + ADB_FIELD_SEP)}$({readlink} -f {EscapeAdbShellString(path)} 2>&1){ADB_FIELD_SEP}"));

        ExecuteDeviceAdbShellCommand(deviceId,
                                     readlinkScript,
                                     out string stdout,
                                     out string stderr,
                                     cancellationToken);

        var linkDict = new Dictionary<string, string>();
        foreach (var line in stdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(ADB_FIELD_SEP, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                ExecuteDeviceAdbShellCommand(deviceId,
                                     $"{readlink} {EscapeAdbShellString(parts[0])}",
                                     out string stdout1,
                                     out string stderr1,
                                     cancellationToken);

                if (stdout1.Split([ADB_FIELD_SEP, .. LINE_SEPARATORS], StringSplitOptions.RemoveEmptyEntries) is var parts1 && parts1.Length > 0)
                {
                    linkDict[parts[0]] = parts1[0];
                }

                continue;
            }

            linkDict[parts[0]] = parts[1];
        }

        var uniqueLinks = linkDict.Values.Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().Select(l => EscapeAdbShellString(l));

        // Get file mode of all unique link targets
        ExecuteDeviceAdbShellCommand(deviceId, "stat", out string statStdout, out string statStderr, cancellationToken, [.. uniqueLinks, .. STAT_LINKMODE_ARGS]);

        var linkTypes = new Dictionary<string, FileType>();
        foreach (var line in statStdout.Split(LINE_SEPARATORS, StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = line.Split(ADB_FIELD_SEP);
            if (parts.Length >= 2 && UInt32.TryParse(parts[1].Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var modeValue))
                linkTypes[parts[0]] = ParseFileMode((UnixFileMode)modeValue);
        }

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

    public static void ListDirectory(string deviceId, string path, ref ConcurrentQueue<FileStat> output, Dispatcher dispatcher, CancellationToken cancellationToken)
    {
        try
        {
            if (ArchivePath.TryParse(path, out var archivePath, out var internalPath, deviceId))
            {
                foreach (var item in ArchiveListing.ListEntries(deviceId, archivePath, internalPath, cancellationToken))
                    output.Enqueue(item);
            }
            else
            {
                foreach (var item in ListDirectoryEntries(deviceId, path, cancellationToken))
                    output.Enqueue(item);
            }
        }
        catch (OperationCanceledException)
        { }
        catch (Exception e)
        {
            var message = e.Message;
            if (!string.IsNullOrEmpty(message))
                message += "\n\n";

            dispatcher.Invoke(() => DialogService.ShowMessage(message + Strings.Resources.S_LS_ERROR,
                                                              Strings.Resources.S_LS_ERROR_TITLE,
                                                              DialogService.DialogIcon.Critical,
                                                              true,
                                                              copyToClipboard: true,
                                                              error: DialogError.ListDirectoryFailed));
        }
    }

    public static IEnumerable<FileStat> ListDirectoryEntries(string deviceId, string path, CancellationToken cancellationToken)
    {
        foreach (string stdoutLine in ExecuteDeviceAdbCommandAsync(deviceId, "ls", cancellationToken, EscapeAdbString(path)))
        {
            var item = CreateFile(path, stdoutLine);

            if (item is not null)
                yield return item.Value;
        }
    }

    public static IEnumerable<FileStat> ListDirectoryRecursive(string deviceId, string path, CancellationToken cancellationToken)
    {
        var queue = new Queue<string>();
        queue.Enqueue(path);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();

            IEnumerable<FileStat> entries;
            try
            {
                entries = ListDirectoryEntries(deviceId, current, cancellationToken);
            }
            catch (ProcessFailedException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                yield return entry;

                if (entry.Type is FileType.Folder)
                    queue.Enqueue(entry.FullPath);
            }
        }
    }

    public static string TranslateDevicePath(string deviceId, string path)
    {
        if (path.StartsWith('~'))
            path = path.Length == 1 ? "/" : path[1..];

        if (path.StartsWith("//"))
            path = path[1..];

        int exitCode = ExecuteDeviceAdbShellCommand(deviceId, "cd", out string stdout, out string stderr, CancellationToken.None, EscapeAdbShellString(path), "&&", "pwd");
        if (exitCode != 0)
        {
            throw new Exception(stderr);
        }
        return stdout.TrimEnd(LINE_SEPARATORS);
    }
}
