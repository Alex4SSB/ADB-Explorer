using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

class LogicalDriveViewModel : DriveViewModel
{
    private LogicalDrive drive;
    protected new LogicalDrive Drive
    {
        get => drive;
        set => Set(ref drive, value);
    }

    public string Size => Drive.Size;
    public string Used => Drive.Used;
    public string Available => Drive.Available;
    public sbyte UsageP => Drive.UsageP;

    public bool UsageWarning => UsageP >= AdbExplorerConst.DRIVE_WARNING;
    public string ID => Drive.ID;


    public LogicalDriveViewModel(LogicalDrive drive) : base(drive)
    {
        Drive = drive;
    }

    public void SetParams(LogicalDriveViewModel other) => SetParams(other.Drive);

    public void SetParams(LogicalDrive other)
    {
        var updatedParams = Drive.SetDriveParams(other.Size, other.Used, other.Available, other.UsageP);
        updatedParams.ForEach(p => OnPropertyChanged(p));
    }

    public void SetExtension(bool isMMC = true) => SetType(isMMC ? DriveType.Expansion : DriveType.External);

    public override string ToString() => DisplayName is null ? ID : DisplayName;
}
