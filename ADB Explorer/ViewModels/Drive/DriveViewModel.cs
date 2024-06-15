using ADB_Explorer.Helpers;
using ADB_Explorer.Models;
using ADB_Explorer.Services;

namespace ADB_Explorer.ViewModels;

public class DriveViewModel : AbstractDrive
{
    #region Full properties

    private Drive drive;
    public Drive Drive
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

    private bool driveEnabled = true;
    public bool DriveEnabled
    {
        get => driveEnabled;
        protected set => Set(ref driveEnabled, value);
    }

    #endregion

    #region Read only properties

    public string Path => Drive.Path;
    public new DriveType Type => Drive.Type;
    public bool IsFUSE => Drive.IsFUSE;
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

    #endregion

    #region Commands

    public BaseAction BrowseCommand { get; private set; }
    public BaseAction SelectCommand { get; private set; }
    
    #endregion

    public DriveViewModel(Drive drive)
    {
        Drive = drive;

        BrowseCommand = new(() => true, () => Data.RuntimeSettings.BrowseDrive = this);
        SelectCommand = new(() => true, () => DriveSelected = true);

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
