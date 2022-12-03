using ADB_Explorer.Models;

namespace ADB_Explorer.ViewModels;

class VirtualDriveViewModel : DriveViewModel
{
    public ulong ItemsCount => ((VirtualDrive)Drive).ItemsCount;

    
    public VirtualDriveViewModel(VirtualDrive drive) : base(drive)
    { }

    public void SetItemsCount(int count) => SetItemsCount((ulong)count);

    public void SetItemsCount(ulong newCount)
    {
        if (((VirtualDrive)Drive).ItemsCount != newCount)
        {
            ((VirtualDrive)Drive).ItemsCount = newCount;
            OnPropertyChanged(nameof(ItemsCount));
        }
    }

    public override string ToString() => $"{Type}";
}
