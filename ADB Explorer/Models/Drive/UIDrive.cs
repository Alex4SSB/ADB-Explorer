namespace ADB_Explorer.Models;

public class UIDrive : AbstractDrive
{
    public UIDrive(Drive drive)
    {
        Drive = drive;
    }

    private Drive drive;
    public Drive Drive
    {
        get => drive;
        set => Set(ref drive, value);
    }

    public string DriveIcon => Drive.Type switch
    {
        DriveType.Root => "\uF259",
        DriveType.Internal => "\uEDA2",
        DriveType.Expansion => "\uE7F1",
        DriveType.External => "\uE88E",
        DriveType.Unknown => "\uE9CE",
        DriveType.Emulated => "\uEDA2",
        DriveType.Trash => "\uE74D",
        DriveType.Temp => "\uE912",
        DriveType.Package => "\uE7B8",
        _ => throw new NotImplementedException(),
    };

    public string DisplayName => AdbExplorerConst.DRIVE_DISPLAY_NAMES[Drive.Type];
    public bool UsageWarning => Drive.UsageP >= AdbExplorerConst.DRIVE_WARNING;


    public class UIDriveEqualityComparer : IEqualityComparer<UIDrive>
    {
        public bool Equals(UIDrive x, UIDrive y) => x.Drive.Equals(y.Drive);

        public int GetHashCode([DisallowNull] UIDrive obj) => throw new NotImplementedException();
    }
}
