using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class DriveViewModel : AbstractDrive
{
    public DriveViewModel(Drive drive)
    {
        Drive = drive;

        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.CollapseDrives) && Data.RuntimeSettings.CollapseDrives)
        {
            DriveSelected = false;
        }
    }

    private Drive drive;
    private Drive Drive
    {
        get => drive;
        set => Set(ref drive, value);
    }

    public string Size => drive.Size;
    public string Used => drive.Used;
    public string Available => drive.Available;
    public sbyte UsageP => drive.UsageP;
    public string Path => drive.Path;
    public new DriveType Type => drive.Type;
    public ulong ItemsCount => drive.ItemsCount;

    private bool driveSelected = false;
    public bool DriveSelected
    {
        get => driveSelected;
        set => Set(ref driveSelected, value);
    }

    public string DriveIcon => Type switch
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

    public bool IsVirtual => Type is DriveType.Trash or DriveType.Temp or DriveType.Package;

    public string DisplayName => AdbExplorerConst.DRIVE_DISPLAY_NAMES[Type];
    public bool UsageWarning => UsageP >= AdbExplorerConst.DRIVE_WARNING;
    public string ID => Drive.ID;

    public void SetParams(DriveViewModel other) => SetParams(other.Drive);

    public void SetParams(Drive other)
    {
        var updatedParams = Drive.SetDriveParams(other.Size, other.Used, other.Available, other.UsageP);
        updatedParams.ForEach(p => OnPropertyChanged(p));
    }

    public void SetExtension(bool isMMC = true) => SetType(isMMC ? DriveType.Expansion : DriveType.External);

    public void SetType(DriveType type)
    {
        if (Drive.Type != type)
        {
            Drive.Type = type;
            OnPropertyChanged(nameof(Type));
        }
    }

    public void SetItemsCount(ulong newCount)
    {
        if (Drive.ItemsCount != newCount)
        {
            Drive.ItemsCount = newCount;
            OnPropertyChanged(nameof(ItemsCount));
        }
    }

    public void SetItemsCount(int count) => SetItemsCount((ulong)count);

    public override string ToString() => DisplayName ?? ID;
}
