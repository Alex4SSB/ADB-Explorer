using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

public partial class VirtualDriveViewModel : DriveViewModel
{
    [ObservableProperty]
    protected new partial VirtualDrive Drive { get; set; }

    public long? ItemsCount => Drive.ItemsCount;

    // Temp drive is under the root filesystem
    public override bool IsFUSE => Drive.Type is DriveType.Temp
        && Data.DevicesObject.Current?.Drives?.Find(d => d.Type is DriveType.Root)?.IsFUSE is true;

    
    public VirtualDriveViewModel(VirtualDrive drive) : base(drive)
    {
        Drive = drive;
    }

    public void SetItemsCount(int? count) => SetItemsCount((long?)count);

    public void SetItemsCount(long? newCount)
    {
        if (Drive.ItemsCount != newCount)
        {
            Drive.ItemsCount = newCount;
            OnPropertyChanged(nameof(ItemsCount));
        }
    }

    public override string ToString() => $"{Type}";
}
