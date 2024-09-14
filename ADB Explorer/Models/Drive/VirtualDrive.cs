namespace ADB_Explorer.Models;

public class VirtualDrive : Drive
{
    private long? itemsCount = 0;
    public long? ItemsCount
    {
        get => itemsCount;
        set => Set(ref itemsCount, value);
    }

    public override bool IsFUSE => Type switch
    {
        // Temp drive is under the root filesystem
        DriveType.Temp => Data.DevicesObject.Current?.Drives?.Find(d => d.Type is DriveType.Root).IsFUSE is true,
        // App drive isn't really a drive, and the recycle bin doesn't allow any of the actions limited on FUSE
        // So it is useless to display the icon
        _ => false,
    };

    public VirtualDrive(string path = "", long itemsCount = 0) : base(path)
    {
        ItemsCount = itemsCount;
    }
}
