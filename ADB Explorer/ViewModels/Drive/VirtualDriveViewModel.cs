namespace ADB_Explorer.ViewModels;

public partial class VirtualDriveViewModel : DriveViewModel
{
    [ObservableProperty]
    protected new partial VirtualDrive Drive { get; set; }

    public long? ItemsCount => Drive.ItemsCount;

    /// <summary>The temp drive's installers are only counted with APK features on: without them, it has no count to show.</summary>
    public string ItemsCountText => Type switch
    {
        AbstractDrive.DriveType.Temp when !Data.Settings.EnableApk => "",
        AbstractDrive.DriveType.Temp => Strings.Plurals.ApkInstallers(ItemsCount),
        AbstractDrive.DriveType.Package => Strings.Plurals.Packages(ItemsCount),
        _ => Strings.Plurals.ItemCount(ItemsCount),
    };

    public void NotifyItemsCountText() => OnPropertyChanged(nameof(ItemsCountText));

    public string? DfMountPoint => Drive.MountPoint;

    public VirtualDriveViewModel(VirtualDrive drive) : base(drive)
    {
        Drive = drive;
    }

    public void SetItemsCount(int? count) => SetItemsCount((long?)count);

    public void SetItemsCount(long? newCount)
    {
        if (Drive.ItemsCount != newCount)
        {
            var emptyChanged = (Drive.ItemsCount is null or <= 0) != (newCount is null or <= 0);
            Drive.ItemsCount = newCount;
            OnPropertyChanged(nameof(ItemsCount));
            OnPropertyChanged(nameof(ItemsCountText));

            if (emptyChanged)
                OnPropertyChanged(nameof(DriveIcon));

            if (Data.RuntimeSettings.SelectedDrive == this && Data.FileActions.IsDriveViewVisible)
                FileActionLogic.UpdateFileActions();

            if (emptyChanged && Type is AbstractDrive.DriveType.Trash)
                NavHistory.RefreshMenuHistory();
        }
    }

    public void UpdateDrive(DriveSnapshot snapshot)
    {
        if (Drive.MountPoint != snapshot.MountPoint)
        {
            Drive.MountPoint = snapshot.MountPoint;
        }
    }

    public override string ToString() => $"{Type}";
}
