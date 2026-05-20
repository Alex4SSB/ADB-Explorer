using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public partial class LogicalDriveViewModel : DriveViewModel
{
    [ObservableProperty]
    protected new partial LogicalDrive Drive { get; set; }

    [ObservableProperty]
    public partial Models.FileSystemInfo? FSInfo { get; set; }

    partial void OnFSInfoChanged(Models.FileSystemInfo? value)
    {
        OnPropertyChanged(nameof(BlockDevice));
        OnPropertyChanged(nameof(FileSystem));
        OnPropertyChanged(nameof(MountPoint));
        OnPropertyChanged(nameof(MountOptions));
    }

    public string BlockDevice => FSInfo?.BlockDev;
    public string FileSystem => FSInfo?.FileSystemType;
    public string MountPoint => FSInfo?.MountPoint;
    public string[] MountOptions => FSInfo?.Options;

    public string Size => Drive.Size;
    public string Used => Drive.Used;
    public string Available => Drive.Available;
    public sbyte UsageP => Drive.UsageP;

    public bool UsageWarning => UsageP >= AdbExplorerConst.DRIVE_WARNING;
    public string ID => Drive.ID;


    public LogicalDriveViewModel(LogicalDrive drive) : base(drive)
    {
        Drive = drive;

        Drive.PropertyChanged += (s, e) =>
        {
            switch (e.PropertyName)
            {
                case nameof(LogicalDrive.Size):
                    OnPropertyChanged(nameof(Size));
                    break;
                case nameof(LogicalDrive.Used):
                    OnPropertyChanged(nameof(Used));
                    break;
                case nameof(LogicalDrive.Available):
                    OnPropertyChanged(nameof(Available));
                    break;
                case nameof(LogicalDrive.UsageP):
                    OnPropertyChanged(nameof(UsageP));
                    OnPropertyChanged(nameof(UsageWarning));
                    break;
            }
        };
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

    public void UpdateDrive(DriveSnapshot snapshot)
    {
        if (Drive.Size != snapshot.Size)
        {
            Drive.Size = snapshot.Size;
            OnPropertyChanged(nameof(Size));
        }

        if (Drive.Used != snapshot.Used)
        {
            Drive.Used = snapshot.Used;
            OnPropertyChanged(nameof(Used));
        }

        if (Drive.Available != snapshot.Available)
        {
            Drive.Available = snapshot.Available;
            OnPropertyChanged(nameof(Available));
        }

        if (Drive.UsageP != snapshot.UsageP)
        {
            Drive.UsageP = snapshot.UsageP;
            OnPropertyChanged(nameof(UsageP));
        }

        if (Drive.FileSystem != snapshot.FileSystem)
        {
            Drive.FileSystem = snapshot.FileSystem;
            OnPropertyChanged(nameof(IsFUSE));
        }
    }

    public void SetExtension(bool isMMC = true) => SetType(isMMC ? DriveType.Expansion : DriveType.External);

    public override string ToString() => DisplayName is null ? ID : DisplayName;
}
