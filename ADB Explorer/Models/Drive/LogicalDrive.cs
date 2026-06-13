using ADB_Explorer.Services;

namespace ADB_Explorer.Models;

public partial class LogicalDrive : Drive
{
    [ObservableProperty]
    public partial string Size { get; set; }

    [ObservableProperty]
    public partial string Used { get; set; }

    [ObservableProperty]
    public partial string Available { get; set; }

    [ObservableProperty]
    public partial sbyte UsageP { get; set; }

    [ObservableProperty]
    public partial string LinkTargetPath { get; set; }

    [ObservableProperty]
    public partial string FileSystem { get; set; } = "";

    public string ID => Path.Count(c => c == '/') > 1 ? Path[(Path.LastIndexOf('/') + 1)..] : Path;


    public LogicalDrive(string size = "",
                        string used = "",
                        string available = "",
                        sbyte usageP = -1,
                        string path = "",
                        bool isMMC = false,
                        bool isEmulator = false,
                        string fileSystem = "")
        : base(path)
    {
        if (usageP is < -1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(usageP));

        Size = size;
        Used = used;
        Available = available;
        UsageP = usageP;

        if (path == "/")
        {
            Type = DriveType.Root;
        }
        else if (AdbExplorerConst.DRIVE_TYPES.Where(kv => kv.Value is DriveType.Internal).Any(kv => kv.Key.Contains(path)))
        {
            Type = DriveType.Internal;
        }
        else if (isMMC)
        {
            Type = DriveType.Expansion;
        }
        else if (isEmulator)
        {
            Type = DriveType.Emulated;
        }

        FileSystem = fileSystem;
    }

    public void UpdateInternalStorage(string deviceId)
    {
        if (Type is not DriveType.Internal)
            return;

        string path = "", target = "";

        var echo = ShellCommands.TranslateCommand("echo");
        var readlink = ShellCommands.TranslateCommand("readlink");

        // readlink -f/-e is unreliable on some devices (e.g. OnePlus Android 9); follow symlink chains manually.
        var resolveScript =
            $"i=0;l=$EXTERNAL_STORAGE;" +
            $"while [ -L \"$l\" ] && [ $i -lt 10 ];do t=$({readlink} \"$l\");case \"$t\" in /*)l=\"$t\";;*)l=\"$(dirname \"$l\")/$t\";;esac;i=$((i+1));done;" +
            $"{echo} \"$l{AdbExplorerConst.ADB_FIELD_SEP}$EXTERNAL_STORAGE\"";

        var task = Task.Run(() =>
        {
            var result = ADBService.ExecuteDeviceAdbShellCommand(deviceId,
                                                                resolveScript,
                                                                out string stdout,
                                                                out string stderr,
                                                                CancellationToken.None);

            if (result == 0)
            {
                var items = stdout.TrimEnd('\r', '\n')
                                  .Split(AdbExplorerConst.ADB_FIELD_SEP, StringSplitOptions.RemoveEmptyEntries);

                if (items.Length == 2 && !string.IsNullOrEmpty(items[1]))
                {
                    target = items[0].Trim();
                    path = items[1].Trim();
                }
                else
                    path = AdbExplorerConst.DRIVE_TYPES.First(d => d.Value is DriveType.Internal).Key;
            }
        });

        task.ContinueWith((t) =>
        {
            App.SafeInvoke(() =>
            {
                Path = path;
                LinkTargetPath = target;
            });
        });
    }

    public static LogicalDrive From(DriveSnapshot snapshot)
    {
        var drive = new LogicalDrive(
            size: snapshot.Size,
            used: snapshot.Used,
            available: snapshot.Available,
            usageP: snapshot.UsageP,
            path: snapshot.Path,
            isEmulator: snapshot.IsEmulator,
            fileSystem: snapshot.FileSystem);

        if (snapshot.Type is not DriveType.Unknown && drive.Type != snapshot.Type)
            drive.Type = snapshot.Type;

        return drive;
    }
}
