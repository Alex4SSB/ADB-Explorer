using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public partial class LogicalDriveViewModel : DriveViewModel
{
    [ObservableProperty]
    protected new partial LogicalDrive Drive { get; set; }

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
            Drive.FileSystem = other.FileSystem;
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
            Drive.FileSystem = snapshot.FileSystem;

        if (Drive.Manufacturer != snapshot.Manufacturer || Drive.VolumeLabel != snapshot.VolumeLabel)
        {
            Drive.Manufacturer = snapshot.Manufacturer;
            Drive.VolumeLabel = snapshot.VolumeLabel;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>
    /// Classifies a drive as SD/expansion or USB/OTG, and records the manufacturer + volume label
    /// (from <see cref="ADBService.DriveMountInfo"/>) so <see cref="DriveViewModel.DisplayName"/>
    /// can show Android's own friendly name instead of the generic "SD card"/"OTG drive" text.
    /// </summary>
    public void SetRemovableInfo(DriveType type, string? manufacturer, string? volumeLabel)
    {
        SetType(type);

        if (Drive.Manufacturer != manufacturer || Drive.VolumeLabel != volumeLabel)
        {
            Drive.Manufacturer = manufacturer;
            Drive.VolumeLabel = volumeLabel;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    /// <summary>
    /// Sets this USB/OTG drive's 1-based position among its device's other USB/OTG drives, shown in
    /// <see cref="DriveViewModel.DisplayName"/> as "(n)" only while more than one is connected.
    /// </summary>
    public void SetDriveIndex(int? index)
    {
        if (Drive.DriveIndex != index)
        {
            Drive.DriveIndex = index;
            OnPropertyChanged(nameof(DisplayName));
        }
    }

    public override string ToString() => DisplayName is null ? ID : DisplayName;
}
