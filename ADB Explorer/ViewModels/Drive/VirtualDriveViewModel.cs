using ADB_Explorer.Models;
using ADB_Explorer.Services;
using ADB_Explorer.Services.AppInfra;

namespace ADB_Explorer.ViewModels;

public partial class VirtualDriveViewModel : DriveViewModel
{
    [ObservableProperty]
    protected new partial VirtualDrive Drive { get; set; }

    public long? ItemsCount => Drive.ItemsCount;

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
            OnPropertyChanged(nameof(DfMountPoint));
        }
    }

    public override string ToString() => $"{Type}";
}
