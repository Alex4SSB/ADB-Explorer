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

    public void UpdateDrive(LogicalDrive other)
    {
        if (Drive.Size != other.Size)
        {
            Drive.Size = other.Size;
            OnPropertyChanged(nameof(Size));
        }

        if (Drive.Used != other.Used)
        {
            Drive.Used = other.Used;
            OnPropertyChanged(nameof(Used));
        }

        if (Drive.Available != other.Available)
        {
            Drive.Available = other.Available;
            OnPropertyChanged(nameof(Available));
        }

        if (Drive.UsageP != other.UsageP)
        {
            Drive.UsageP = other.UsageP;
            OnPropertyChanged(nameof(UsageP));
        }

        if (Drive.FileSystem != other.FileSystem)
        {
            Drive.FileSystem = other.FileSystem;
            OnPropertyChanged(nameof(IsFUSE));
        }
    }

    public void SetExtension(bool isMMC = true) => SetType(isMMC ? DriveType.Expansion : DriveType.External);

    public override string ToString() => DisplayName is null ? ID : DisplayName;
}
