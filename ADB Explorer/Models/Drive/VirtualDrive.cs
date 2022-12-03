namespace ADB_Explorer.Models;

public class VirtualDrive : Drive
{
    private ulong itemsCount = 0;
    public ulong ItemsCount
    {
        get => itemsCount;
        set => Set(ref itemsCount, value);
    }

    public VirtualDrive(string path = "", ulong itemsCount = 0) : base(path)
    {
        ItemsCount = itemsCount;
    }
}
