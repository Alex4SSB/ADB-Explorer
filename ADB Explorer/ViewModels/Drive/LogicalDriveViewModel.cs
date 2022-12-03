using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

class LogicalDriveViewModel : DriveViewModel
{
    public string Size => ((LogicalDrive)Drive).Size;
    public string Used => ((LogicalDrive)Drive).Used;
    public string Available => ((LogicalDrive)Drive).Available;
    public sbyte UsageP => ((LogicalDrive)Drive).UsageP;

    public bool UsageWarning => UsageP >= AdbExplorerConst.DRIVE_WARNING;
    public string ID => ((LogicalDrive)Drive).ID;


    public LogicalDriveViewModel(LogicalDrive drive) : base(drive)
    { }

    public void SetParams(LogicalDriveViewModel other) => SetParams((LogicalDrive)other.Drive);

    public void SetParams(LogicalDrive other)
    {
        var updatedParams = ((LogicalDrive)Drive).SetDriveParams(other.Size, other.Used, other.Available, other.UsageP);
        updatedParams.ForEach(p => OnPropertyChanged(p));
    }

    public void SetExtension(bool isMMC = true) => SetType(isMMC ? DriveType.Expansion : DriveType.External);

    public override string ToString() => DisplayName is null ? ID : DisplayName;
}
