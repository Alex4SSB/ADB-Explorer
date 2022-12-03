using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class DriveViewModel : AbstractDrive
{
    private Drive drive;
    protected Drive Drive
    {
        get => drive;
        set => Set(ref drive, value);
    }

    private bool driveSelected = false;
    public bool DriveSelected
    {
        get => driveSelected;
        set => Set(ref driveSelected, value);
    }

    public BrowseCommand BrowseCommand { get; private set; }
    public SelectCommand SelectCommand { get; private set; }

    public string Path => Drive.Path;
    public new DriveType Type => Drive.Type;
    public string DisplayName => AdbExplorerConst.DRIVE_DISPLAY_NAMES[Type];

    public string DriveIcon => Type switch
    {
        DriveType.Root => "\uF259",
        DriveType.Internal => "\uEDA2",
        DriveType.Expansion => "\uE7F1",
        DriveType.External => "\uE88E",
        DriveType.Unknown => null,
        DriveType.Emulated => "\uEDA2",
        DriveType.Trash => "\uE74D",
        DriveType.Temp => "\uE912",
        DriveType.Package => "\uE7B8",
        _ => throw new NotImplementedException(),
    };


    public DriveViewModel(Drive drive)
    {
        Drive = drive;

        BrowseCommand = new(this);
        SelectCommand = new(this);

        Data.RuntimeSettings.PropertyChanged += RuntimeSettings_PropertyChanged;
    }

    private void RuntimeSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppRuntimeSettings.CollapseDrives) && Data.RuntimeSettings.CollapseDrives)
        {
            DriveSelected = false;
        }
    }

    public void SetType(DriveType type)
    {
        if (Drive.Type != type)
        {
            Drive.Type = type;
            OnPropertyChanged(nameof(Type));
        }
    }
}
