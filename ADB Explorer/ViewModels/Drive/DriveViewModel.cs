using ADB_Explorer.Helpers;
using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public partial class DriveViewModel : AbstractDrive, IBrowserItem
{
    #region Full properties

    [ObservableProperty]
    public partial Drive Drive { get; set; }

    [ObservableProperty]
    public partial bool IsSelected { get; set; } = false;

    [ObservableProperty]
    public partial bool DriveEnabled { get; protected set; } = true;

    #endregion

    #region Read only properties

    public string Path => Drive.Path;
    public string? LinkTargetPath => (Drive as LogicalDrive)?.LinkTargetPath;

    public new DriveType Type => Drive.Type;
    public virtual bool IsFUSE => Drive.IsFUSE;

    public new string DisplayName => Drive.DisplayName;

    public string DriveIcon => GetDriveIcon(Type);

    public static string GetDriveIcon(DriveType type) => type switch
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
    
    #endregion

    public DriveViewModel(Drive drive)
    {
        Drive = drive;

        BrowseCommand = new(() => true, () => Data.RuntimeSettings.BrowseDrive = this);
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
