using ADB_Explorer.Controls;
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

    #endregion

    #region Read only properties

    public string Path => Drive.Path;
    public string? LinkTargetPath => (Drive as LogicalDrive)?.LinkTargetPath;

    public new DriveType Type => Drive.Type;

    public new string DisplayName => Drive.DisplayName;

    public DriveRestrictions Restrictions => DriveRestrictions.From(FSInfo?.Options);

    public bool HasDriveRestrictions => Restrictions.HasAny;

    public string RestrictionsTooltip => Restrictions.GetTooltipText();

    [ObservableProperty]
    public partial Models.FileSystemInfo? FSInfo { get; set; }

    partial void OnFSInfoChanged(Models.FileSystemInfo? value)
    {
        OnPropertyChanged(nameof(BlockDevice));
        OnPropertyChanged(nameof(FileSystem));
        OnPropertyChanged(nameof(MountPoint));
        OnPropertyChanged(nameof(MountOptions));
        OnPropertyChanged(nameof(Restrictions));
        OnPropertyChanged(nameof(HasDriveRestrictions));
        OnPropertyChanged(nameof(RestrictionsTooltip));
    }

    public string BlockDevice => FSInfo?.BlockDev;
    public string FileSystem => FSInfo?.FileSystemType;
    public string MountPoint => FSInfo?.MountPoint;
    public string[] MountOptions => FSInfo?.Options;

    // Read at 2x the display size so the icon stays sharp under monitor scaling above 100%.
    public BaseIcon? DriveIcon => GetIcon(48, pixelSize: 96);

    /// <summary>
    /// Same DLL-extracted icon shown in the details pane / navigation tree, displayed at <paramref name="size"/>
    /// pixels but read from the icon source at <paramref name="pixelSize"/> (defaults to <paramref name="size"/>)
    /// so it can be supersampled for crisp rendering under monitor DPI scaling.
    /// </summary>
    public BaseIcon? GetIcon(double size = 32, int? pixelSize = null)
    {
        var trashEmpty = this is VirtualDriveViewModel { ItemsCount: 0 };
        return GetDriveIcon(Type, size, trashEmpty, pixelSize);
    }

    public static BaseIcon? GetDriveIcon(DriveType type, double size = 32, bool trashEmpty = false, int? pixelSize = null)
    {
        if (type is DriveType.Unknown)
            return null;

        return new(FileToIconConverter.GetDriveIcon(type, pixelSize ?? (int)size, trashEmpty), size);
    }

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
            OnPropertyChanged(nameof(DriveIcon));
            OnPropertyChanged(nameof(DisplayName));
        }
    }
}
